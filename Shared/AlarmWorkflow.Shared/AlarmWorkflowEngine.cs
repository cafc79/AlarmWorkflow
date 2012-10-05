using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml;
using AlarmWorkflow.Shared.Core;
using AlarmWorkflow.Shared.Diagnostics;
using AlarmWorkflow.Shared.Extensibility;

namespace AlarmWorkflow.Shared
{
    /// <summary>
    /// Represents the main entry point of the AlarmWorkflow application. This class manages the parsing of the Alarmfaxes and is responsible for calling the configured jobs.
    /// </summary>
    public sealed class AlarmWorkflowEngine : IDisposable
    {
        #region Fields

        private Thread _workingThread;

        private DirectoryInfo _faxPath;
        private DirectoryInfo _archivePath;
        private DirectoryInfo _analysisPath;

        private OcrSoftware _useOCRSoftware;
        private DirectoryInfo _ocrPath;
        private IParser _parser;

        private Dictionary<string, string> _replaceDictionary;

        private List<IJob> _jobs;
        private IOperationStore _operationStore;

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether or not the AlarmWorkflow-process is running.
        /// </summary>
        public bool IsStarted { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the AlarmWorkflowEngine class.
        /// Constructor is reading the XML File, and safe the Settings in to the WorkingThread Instance.
        /// </summary>
        public AlarmWorkflowEngine()
        {
            _jobs = new List<IJob>();

            InitializeSettings();
            InitializeReplaceDictionary();
            InitializeJobs();
        }

        #endregion

        #region Methods

        private void InitializeSettings()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\Config\AlarmWorkflow.xml");

            // Thread Einstellungen initiieren
            string faxPath = doc.GetElementsByTagName("FaxPath")[0].InnerText;
            string archivePath = doc.GetElementsByTagName("ArchivePath")[0].InnerText;
            string analysisPath = doc.GetElementsByTagName("AnalysisPath")[0].InnerText;
            string ocr = doc.GetElementsByTagName("OCRSoftware")[0].Attributes["type"].InnerText;
            string ocrpath = doc.GetElementsByTagName("OCRSoftware")[0].Attributes["path"].InnerText;
            string parser = doc.GetElementsByTagName("AlarmfaxParser")[0].InnerText;
            string operationStore = doc.GetElementsByTagName("OperationStore")[0].InnerText;

            _faxPath = new DirectoryInfo(faxPath);
            _archivePath = new DirectoryInfo(archivePath);
            _analysisPath = new DirectoryInfo(analysisPath);
            if (ocr.ToUpperInvariant() == "TESSERACT")
            {
                _useOCRSoftware = OcrSoftware.Tesseract;
                if (string.IsNullOrWhiteSpace(ocrpath)) { ocrpath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\tesseract"; }
            }
            else
            {
                _useOCRSoftware = OcrSoftware.Cuneiform;
                if (string.IsNullOrWhiteSpace(ocrpath)) { ocrpath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\cuneiform"; }
            }
            _ocrPath = new DirectoryInfo(ocrpath);
            if (!_ocrPath.Exists)
            {
                throw new DirectoryNotFoundException(string.Format("The OCR software '{0}' was suggested to be found in path '{1}', which doesn't exist!", _useOCRSoftware, _ocrPath.FullName));
            }

            // Import parser with the given name/alias
            _parser = ExportedTypeLibrary.Import<IParser>(parser);
            Logger.Instance.LogFormat(LogType.Info, this, "Using parser '{0}'.", _parser.GetType().FullName);
            _operationStore = ExportedTypeLibrary.Import<IOperationStore>(operationStore);
            Logger.Instance.LogFormat(LogType.Info, this, "Using operation store '{0}'.", _operationStore.GetType().FullName);
        }

        private void InitializeJobs()
        {
            foreach (IJob job in ExportedTypeLibrary.ImportAll<IJob>())
            {
                string jobName = job.GetType().Name;
                Logger.Instance.LogFormat(LogType.Info, this, "Initializing job type '{0}'...", jobName);

                try
                {
                    job.Initialize();
                    _jobs.Add(job);

                    Logger.Instance.LogFormat(LogType.Info, this, "Job type '{0}' initialization successful.", jobName);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogFormat(LogType.Error, this, "An error occurred while initializing job type '{0}'. The error message was: {1}", jobName, ex.Message);
                }
            }
        }

        private void InitializeReplaceDictionary()
        {
            _replaceDictionary = new Dictionary<string, string>(16);

            XmlDocument doc = new XmlDocument();
            doc.Load(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\Config\ReplaceDictionary.xml");

            XmlNode replacingNode = doc.GetElementsByTagName("Dictionary")[0];
            foreach (XmlNode rpn in replacingNode.ChildNodes)
            {
                _replaceDictionary[rpn.Attributes["Old"].InnerText] = rpn.Attributes["New"].InnerText;
            }
        }

        /// <summary>
        /// Makes sure that the required directories exist and we don't run into unnecessary exceptions.
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                if (!_faxPath.Exists)
                {
                    _faxPath.Create();
                }
                if (!_archivePath.Exists)
                {
                    _archivePath.Create();
                }
                if (!_analysisPath.Exists)
                {
                    _analysisPath.Create();
                }
            }
            catch (IOException)
            {
                Logger.Instance.LogFormat(LogType.Warning, this, "Could not create any of the default directories. Try running the process as Administrator, or create the directories in advance.");
            }
        }

        /// <summary>
        /// Processes all faxes that are within the configured fax-directory.
        /// </summary>
        private void ProcessLoop()
        {
            EnsureDirectoriesExist();

            while (true)
            {
                FileInfo[] files = _faxPath.GetFiles("*.tif", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    foreach (FileInfo file in files)
                    {
                        ProcessFile(file);
                    }
                }
                Thread.Sleep(3000);
            }
        }

        private void ProcessFile(FileInfo file)
        {
            string analyseFileName = DateTime.Now.ToString("yyyyMMddHHmmss");
            bool fileIsMoved = false;
            int tried = 0;
            while (!fileIsMoved)
            {
                tried++;
                try
                {
                    file.MoveTo(Path.Combine(_archivePath.FullName, analyseFileName + ".TIF"));
                    fileIsMoved = true;
                }
                catch (IOException ex)
                {
                    if (tried < 60)
                    {
                        Logger.Instance.LogFormat(LogType.Warning, this, "Coudn't move file. Try {0} of 10!", tried);
                        Thread.Sleep(200);
                        fileIsMoved = false;
                    }
                    else
                    {
                        Logger.Instance.LogFormat(LogType.Error, this, "Coundn't move file.\n" + ex.ToString());
                        return;
                    }
                }
            }

            // One catch of TIFF is that it may contain multiple pages! This is by itself no problem, but the OCR software
            // may not be able to recognize this. So we do the following:
            // 1. We read the TIFF, and if it is a multipage-tiff-file...
            // 2. ... we will split each page into an own file (done in the method) ...
            // 3. ... and THEN we will scan each "page" and concat them together, so it appears to the parser as one file.
            List<string> analyzedLines = new List<string>();
            foreach (string imageFile in Utilities.GetMergedTifFileNames(Path.Combine(_archivePath.FullName, analyseFileName + ".TIF")))
            {
                string intendedNewFileName = Path.Combine(_analysisPath.FullName, Path.GetFileNameWithoutExtension(imageFile) + ".txt");

                // Host the configured OCR-software in a new process and run it
                using (Process proc = new Process())
                {
                    proc.EnableRaisingEvents = false;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.WorkingDirectory = _ocrPath.FullName;

                    switch (_useOCRSoftware)
                    {
                        case OcrSoftware.Tesseract:
                            {
                                proc.StartInfo.FileName = Path.Combine(proc.StartInfo.WorkingDirectory, "tesseract.exe");
                                proc.StartInfo.Arguments = file.DirectoryName + "\\" + analyseFileName + ".bmp " + intendedNewFileName + " -l deu";

                                // Correct txt path for tesseract (it will append .txt under windows always)
                                intendedNewFileName += ".txt";
                            }
                            break;
                        case OcrSoftware.Cuneiform:
                        default:
                            {
                                proc.StartInfo.FileName = Path.Combine(proc.StartInfo.WorkingDirectory, "cuneiform.exe");
                                proc.StartInfo.Arguments = "-l ger --singlecolumn -o " + intendedNewFileName + " " + imageFile;
                            }
                            break;
                    }

                    try
                    {
                        proc.Start();
                        proc.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogFormat(LogType.Warning, this, "Error while the ocr Prozess: " + ex.ToString());
                        return;
                    }

                    // After the file has been parsed, read it back in ...
                    // ... fetch all lines ...
                    foreach (string preParsedLine in File.ReadAllLines(intendedNewFileName))
                    {
                        string tmp = preParsedLine;
                        foreach (var pair in _replaceDictionary)
                        {
                            tmp = tmp.Replace(pair.Key, pair.Value);
                        }
                        // ... and add it to the list
                        analyzedLines.Add(tmp);
                    }
                }
            }

            Operation operation = null;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                // Try to parse the operation. If parsing failed, ignore this but write to the log file!
                Logger.Instance.LogFormat(LogType.Info, this, "Begin parsing incoming operation...");

                operation = _parser.Parse(analyzedLines.ToArray());

                sw.Stop();
                Logger.Instance.LogFormat(LogType.Debug, this, "Parsed operation in '{0}' milliseconds.", sw.ElapsedMilliseconds);

                // Grant the operation a new Id
                operation.Id = _operationStore.GetNextOperationId();

                foreach (IJob job in _jobs)
                {
                    // Run the job. If the job fails, ignore that exception as well but log it too!
                    try
                    {
                        job.DoJob(operation);
                    }
                    catch (Exception ex)
                    {
                        // Be careful when processing the jobs, we don't want a malicious job to terminate the process!
                        Logger.Instance.LogFormat(LogType.Warning, this, string.Format("An error occurred while processing job '{0}'!", job.GetType().Name));
                        Logger.Instance.LogException(this, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogFormat(LogType.Warning, this, "An exception occurred while processing the alarmfax!");
                Logger.Instance.LogException(this, ex);
            }
        }

        /// <summary>
        /// Starts the monitor thread, which is waiting for a new Alarm.
        /// </summary>
        public void Start()
        {
            if (IsStarted)
            {
                return;
            }

            _workingThread = new Thread(new ThreadStart(ProcessLoop));
            _workingThread.Start();
            Logger.Instance.LogFormat(LogType.Info, this, "Started Service");

            IsStarted = true;
        }

        /// <summary>
        /// Stops the Thread.
        /// </summary>
        public void Stop()
        {
            if (!IsStarted)
            {
                return;
            }

            Logger.Instance.LogFormat(LogType.Info, this, "Stopping Service");
            if (_workingThread != null)
            {
                _workingThread.Abort();
            }

            Logger.Instance.LogFormat(LogType.Info, this, "Stopped Service");

            IsStarted = false;
        }

        /// <summary>
        /// Returns the <see cref="IOperationStore"/>-instance that is used.
        /// </summary>
        /// <returns></returns>
        public IOperationStore GetOperationStore()
        {
            return _operationStore;
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Stop();

            // TODO: Dispose jobs!

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}