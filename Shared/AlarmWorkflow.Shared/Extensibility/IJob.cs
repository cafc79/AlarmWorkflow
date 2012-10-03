﻿using System.Xml.XPath;
using AlarmWorkflow.Shared.Core;

// TODO: Support Shutdown() method!

namespace AlarmWorkflow.Shared.Extensibility
{
    /// <summary>
    /// Thid interface descibes an job interface. Every job has to implement this Interface.
    /// </summary>
    public interface IJob : IExtensionObject
    {
        /// <summary>
        /// Gets the error message.
        /// </summary>
        string ErrorMessage { get; }

        /// <summary>
        /// Called to initialize the job when it gets added to the job queue that runs on each processed fax.
        /// </summary>
        void Initialize();
        /// <summary>
        /// This methode do the jobs job.
        /// </summary>
        /// <param name="operation">Current operation.</param>
        /// <returns>False when an error occured, otherwise true.</returns>
        bool DoJob(Operation operation);
    }
}