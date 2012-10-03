﻿using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using AlarmWorkflow.Shared.Core;
using AlarmWorkflow.Shared.Extensibility;
using MySql.Data.MySqlClient;

// TODO: needs to be updated (new "Id" and "Created" columns)!

namespace AlarmWorkflow.Job.MySqlDatabaseJob
{
    /// <summary>
    /// Implements a job, who saves all the operation data to a MySQL database.
    /// </summary>
    public class DatabaseJob : IJob
    {
        #region private members
        /// <summary>
        /// Saves the errormsg, if an error occured.
        /// </summary>
        private string errormsg;

        /// <summary>
        /// The database user.
        /// </summary>
        private string user;

        /// <summary>
        /// The database users passoword.
        /// </summary>
        private string pwd;

        /// <summary>
        /// Name of the database.
        /// </summary>
        private string database;

        /// <summary>
        /// URL o the MySQL server.
        /// </summary>
        private string server;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the DatabaseJob class.
        /// </summary>
        public DatabaseJob()
        {
        }

        #endregion

        #region IJob Members

        string IJob.ErrorMessage
        {
            get
            {
                return this.errormsg;
            }
        }

        void IJob.Initialize()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\Config\MySqlDatabaseJobConfiguration.xml");

            IXPathNavigable settings = doc.CreateNavigator().SelectSingleNode("DataBase");
            XPathNavigator nav = settings.CreateNavigator();
            this.database = nav.SelectSingleNode("DBName").InnerXml;
            this.user = nav.SelectSingleNode("UserID").InnerXml;
            this.pwd = nav.SelectSingleNode("UserPWD").InnerXml;
            this.server = nav.SelectSingleNode("DBServer").InnerXml;
        }

        bool IJob.DoJob(Operation einsatz)
        {
            this.errormsg = string.Empty;
            try
            {
                using (MySqlConnection conn = new MySqlConnection("Persist Security Info=False;database=" + this.database + ";server=" + this.server + ";user id=" + this.user + ";Password=" + this.pwd))
                {
                    conn.Open();
                    if (conn.State != System.Data.ConnectionState.Open)
                    {
                        this.errormsg = "Coud not open SQL Connection";
                        return false;
                    }

                    // TODO: This string contains CustomData. When actually using this job this should be revised to NOT use any custom data (or make it extensible)!
                    string cmdText = "INSERT INTO tb_einstaz (Einsatznr, Einsatzort, Einsatzplan, Hinweis, Kreuzung, Meldebild, Mitteiler, Objekt, Ort, Strasse, Stichwort) VALUES ('" + einsatz.OperationNumber + "', '" + einsatz.Location + "', '" + einsatz.CustomData["PlanOfAction"] + "', '" + einsatz.Comment + "', '" + einsatz.CustomData["Intersection"] + "', '" + einsatz.CustomData["Picture"] + "', '" + einsatz.Messenger + "', '" + einsatz.Property + "', '" + einsatz.City + "', '" + einsatz.Street + "', '" + einsatz.Keyword + "')";
                    MySqlCommand cmd = new MySqlCommand(cmdText, conn);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                this.errormsg = ex.ToString();
                return false;
            }

            return true;
        }

        #endregion
    }
}