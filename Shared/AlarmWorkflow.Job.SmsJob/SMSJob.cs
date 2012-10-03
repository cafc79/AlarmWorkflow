﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.XPath;
using AlarmWorkflow.Shared.Core;
using AlarmWorkflow.Shared.Extensibility;

namespace AlarmWorkflow.Job.SmsJob
{
    /// <summary>
    /// Implements a Job, that sends SMS with the sms77.de service.
    /// </summary>
    public class SmsJob : IJob
    {
        #region private member

        private string errormsg;
        private List<string> numbers;
        private string username;
        private string password;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SmsJob"/> class.
        /// </summary>
        public SmsJob()
        {
            this.numbers = new List<string>();
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

        bool IJob.DoJob(Operation einsatz)
        {
            this.errormsg = string.Empty;
            // TODO: This string contains CustomData. When actually using this job this should be revised to NOT use any custom data (or make it extensible)!
            string text = "Einsatz:%20" + SmsJob.PrepareString(einsatz.City.Substring(0, einsatz.City.IndexOf(" ", StringComparison.Ordinal))) + "%20" + SmsJob.PrepareString((string)einsatz.CustomData["Picture"]) + "%20" + SmsJob.PrepareString(einsatz.Comment) + "%20Strasse:%20" + SmsJob.PrepareString(einsatz.Street);
            foreach (string number in this.numbers)
            {
                try
                {
                    HttpWebRequest msg = (HttpWebRequest)System.Net.WebRequest.Create(new Uri("http://gateway.sms77.de/?u=" + this.username + "&p=" + this.password + "&to=" + number + "&text=" + text + "&type=basicplus"));
                    HttpWebResponse resp = (HttpWebResponse)msg.GetResponse();
                    Stream resp_steam = resp.GetResponseStream();
                    using (StreamReader streamreader = new StreamReader(resp_steam, Encoding.UTF8))
                    {
                        string response = streamreader.ReadToEnd();
                        if (response != "100")
                        {
                            this.errormsg += "Fehler von sms77: " + response;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.errormsg = ex.ToString();
                }
            }

            return string.IsNullOrEmpty(this.errormsg);
        }

        void IJob.Initialize()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\Config\SmsJobConfiguration.xml");

            IXPathNavigable settings = doc.CreateNavigator().SelectSingleNode("SMS");

            XPathNavigator nav = settings.CreateNavigator();
            if (nav.UnderlyingObject is XmlElement)
            {
                this.username = ((XmlElement)nav.UnderlyingObject).Attributes["username"].Value;
                this.password = ((XmlElement)nav.UnderlyingObject).Attributes["password"].Value;
                foreach (XmlNode node in ((XmlElement)nav.UnderlyingObject).FirstChild.SelectNodes("Nummer")) // HACK: FirstChild hier falsch
                {
                    this.numbers.Add(node.InnerText);
                }
            }
            else
            {
                throw new ArgumentException("Settings is not an XmlElement");
            }
        }

        /// <summary>
        /// This methode url encodes a given string.
        /// </summary>
        /// <param name="str">The string that must be URL encoded.</param>
        /// <returns>The URL encoded string.</returns>
        private static string PrepareString(string str)
        {
            return HttpUtility.UrlEncode(str);
        }

        #endregion

    }
}