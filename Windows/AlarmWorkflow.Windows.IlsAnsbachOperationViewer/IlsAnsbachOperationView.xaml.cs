﻿using System.Windows.Controls;
using AlarmWorkflow.Shared.Core;
using AlarmWorkflow.Windows.UI.Extensibility;

namespace AlarmWorkflow.Windows.IlsAnsbachOperationViewer
{
    /// <summary>
    /// Interaction logic for IlsAnsbachOperationView.xaml
    /// </summary>
    [Export("IlsAnsbachOperationViewer", typeof(IOperationViewer))]
    public partial class IlsAnsbachOperationView : UserControl, IOperationViewer
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="IlsAnsbachOperationView"/> class.
        /// </summary>
        public IlsAnsbachOperationView()
        {
            InitializeComponent();
        }

        #endregion

        #region IOperationViewer Members

        System.Windows.FrameworkElement IOperationViewer.Create()
        {
            return this;
        }

        void IOperationViewer.OnOperationChanged(Operation operation)
        {

        }

        #endregion
    }
}