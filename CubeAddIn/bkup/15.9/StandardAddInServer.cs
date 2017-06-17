using System;
using System.Runtime.InteropServices;
using Inventor;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CubeAddIn
{
    /// <summary>
    /// This is the primary AddIn Server class that implements the ApplicationAddInServer interface
    /// that all Inventor AddIns are required to implement. The communication between Inventor and
    /// the AddIn is via the methods on this interface.
    /// </summary>
    [GuidAttribute("6be86024-98ff-49cf-8681-9c8a0120f883")]
    public class StandardAddInServer : Inventor.ApplicationAddInServer
    {

        // Inventor application object.
        private Inventor.Application m_inventorApplication;
        private Inventor.ButtonDefinition buttonDef;
        CubeForm cube;
        Task formTask;

        public StandardAddInServer()
        {
        }

        #region ApplicationAddInServer Members

        public void Activate(Inventor.ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            // This method is called by Inventor when it loads the addin.
            // The AddInSiteObject provides access to the Inventor Application object.
            // The FirstTime flag indicates if the addin is loaded for the first time.

            // Initialize AddIn members.
            m_inventorApplication = addInSiteObject.Application;
            //TODO: switch to inventor integration
            formTask = new Task(delegate
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                //in case it's not first operation
                try
                {
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                }
                catch (InvalidOperationException ex)
                {                    
                }
                //must be initialized after initialization!
                cube = new CubeForm();

                System.Windows.Forms.Application.Run(cube);
            });
            formTask.Start();
        }

        public void Deactivate()
        {
            // This method is called by Inventor when the AddIn is unloaded.
            // The AddIn will be unloaded either manually by the user or
            // when the Inventor session is terminated

            // TODO: Add ApplicationAddInServer.Deactivate implementation

            // Release objects.
            m_inventorApplication = null;
            //make sure we didn't close already
            if (!cube.IsDisposed)
            {
                cube.Invoke(new EventHandler(delegate {
                    cube.Close();
                }));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ExecuteCommand(int commandID)
        {
            // Note:this method is now obsolete, you should use the 
            // ControlDefinition functionality for implementing commands.
        }

        public object Automation
        {
            // This property is provided to allow the AddIn to expose an API 
            // of its own to other programs. Typically, this  would be done by
            // implementing the AddIn's API interface in a class and returning 
            // that class object through this property.

            get
            {
                // TODO: Add ApplicationAddInServer.Automation getter implementation
                return null;
            }
        }

        #endregion

    }
}
