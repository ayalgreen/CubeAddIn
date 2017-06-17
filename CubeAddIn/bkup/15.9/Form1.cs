//(C) Copyright 2012 by Autodesk, Inc. 

//Permission to use, copy, modify, and distribute this software
//in object code form for any purpose and without fee is hereby
//granted, provided that the above copyright notice appears in
//all copies and that both that copyright notice and the limited
//warranty and restricted rights notice below appear in all
//supporting documentation.

//AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS. 
//AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
//MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK,
//INC. DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL
//BE UNINTERRUPTED OR ERROR FREE.

//Use, duplication, or disclosure by the U.S. Government is
//subject to restrictions set forth in FAR 52.227-19 (Commercial
//Computer Software - Restricted Rights) and DFAR 252.227-7013(c)
//(1)(ii)(Rights in Technical Data and Computer Software), as
//applicable.


//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Data;
//using System.Drawing;
//using System.Linq;

using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Inventor;
using System.IO.Ports;
using System.Windows.Media.Media3D;

namespace CubeAddIn
{
    public partial class CubeForm : Form
    {
        //Constants
        int BAUD_RATE = 38400;
        //TODO auto serial find
        string SERIAL_PORT = "COM6";
        int FPS = 100;
        double MAX_THETA_DIFF = 0.05;
        double MAX_AXIS_DIFF = 0.0005;

        //delegates
        public delegate void SimpleDelegate();
        public delegate void DataProcessDelegate(byte[] buffer);
        public delegate void CameraLockDelegate(Vector3D a, double theta);

        //inventor vars
        Inventor.Application _invApp;
        Inventor.Camera cam;
        TransientGeometry tg;
        bool _startedByForm = false;
        bool inventorRunning = false;
        Timer inventorFrameTimer;
        //TODO decide what to do with dist
        double camDist = 10;

        //comm vars
        SerialPort serialPort1 = new SerialPort();
        char[] teapotPacket = new char[14];  // InvenSense Teapot packet
        int serialCount = 0;                 // current packet byte position
        bool synced = false;
        Timer pingTimer = new Timer();
        //TODO: This.. 
        Timer noDataRecivedTimer = new Timer();
        char[] buff = { 'r' };
        char lastPackedID = (char)0;

        //static vars
        Quaternion quat = new Quaternion(1, 0, 0, 0);
        Quaternion[] oldQuats ={ new Quaternion(1, 0, 0, 0) };
        double[] q = new Double[4];
        double[] gravity = new Double[3];
        double[] euler = new Double[3];
        double[] ypr = new Double[3];
        bool formClose = false;
                
        public CubeForm()
        {
            InitializeComponent();
            formClose = false;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(CloseHandler);
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                comboBoxPorts.Items.Add(port);
            }
            inventorFrameTimer = new Timer();
            inventorFrameTimer.Tick += new EventHandler(LockedRotate);
            inventorFrameTimer.Interval = (int)(1000 / FPS);

            //TODO close app on inventor close?
            StartInventor();
            OpenPort();
            pingTimer = new Timer();
            pingTimer.Tick += new EventHandler(Ping);
            pingTimer.Interval = 2000;
            pingTimer.Start();
        }

        private void StartInventor()
        {
            try
            {
                _invApp = (Inventor.Application)Marshal.GetActiveObject("Inventor.Application");
                inventorRunning = true;
            }
            catch (Exception ex)
            {
                try
                {
                    Type invAppType = Type.GetTypeFromProgID("Inventor.Application");

                    _invApp = (Inventor.Application)System.Activator.CreateInstance(invAppType);
                    _invApp.Visible = true;

                    //Note: if the Inventor session is left running after this
                    //form is closed, there will still an be and Inventor.exe 
                    //running. We will use this Boolean to test in Form1.Designer.cs 
                    //in the dispose method whether or not the Inventor App should
                    //be shut down when the form is closed.
                    _startedByForm = true;
                    inventorRunning = true;

                }
                catch (Exception ex2)
                {
                    MessageBox.Show(ex2.ToString());
                    MessageBox.Show("Unable to get or start Inventor");
                }
            }
        }

        private void OpenPort()
        {
            serialPort1 = new SerialPort();
            serialPort1.PortName = SERIAL_PORT;
            serialPort1.BaudRate = BAUD_RATE;
            serialPort1.ParityReplace = (byte)0;
            serialPort1.DtrEnable = true;
            serialPort1.DataReceived += new SerialDataReceivedEventHandler(SerialPort1DataReceived);
            serialPort1.Open();
        }
        
        //Method for reading from serial port and passing on to InvokedOnData (as handler on form-thread)
        private void SerialPort1DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            while (serialPort1.BytesToRead > 0)
            {
                byte[] buffer = new byte[serialPort1.BytesToRead];
                serialPort1.Read(buffer, 0, buffer.Length);
                //avoid exception in case form had already closed
                if (formClose)
                {
                    return;
                }
                //TODO:need to make sure Handle was created
                BeginInvoke(new DataProcessDelegate(InvokedOnData), buffer);     
                return;
            }
        }

        //form-thread method for parsing received data and then calling DisplayFromPort()
        private void InvokedOnData(byte[] buffer)
        {
            foreach (byte b in buffer)
            {
                int ch = b;
                if (!synced && ch != '$')
                {
                    Console.Write((char)ch);
                    continue;  // initial synchronization - also used to resync/realign if needed
                }
                if (!synced)
                {
                    Console.WriteLine("Synced!");
                }
                synced = true;

                if ((serialCount == 1 && ch != 2)
                    || (serialCount == 12 && ch != '\r')
                    || (serialCount == 13 && ch != '\n'))
                {
                    serialCount = 0;
                    synced = false;
                    continue; 
                }

                if (serialCount > 0 || ch == '$')
                {
                    teapotPacket[serialCount++] = (char)ch;
                    if (serialCount == 14)
                    {
                        serialCount = 0; // restart packet byte position
                        //needed for simple auto lock/unlock mechanism, as each packet is currently sent twice
                        if (teapotPacket[11] != lastPackedID)
                        {
                            lastPackedID = teapotPacket[11];
                        }
                        else
                        {
                            continue;
                        }                 

                        // get quaternion from data packet
                        q[0] = ((teapotPacket[2] << 8) | teapotPacket[3]) / 16384.0f;
                        q[1] = ((teapotPacket[4] << 8) | teapotPacket[5]) / 16384.0f;
                        q[2] = ((teapotPacket[6] << 8) | teapotPacket[7]) / 16384.0f;
                        q[3] = ((teapotPacket[8] << 8) | teapotPacket[9]) / 16384.0f;
                        for (int i = 0; i < 4; i++) if (q[i] >= 2) q[i] = -4 + q[i];

                        // set our quaternion to new data
                        // adjusted to Inventor Coordinate System
                        oldQuats[0] = quat;
                        quat = new Quaternion(q[0], -q[2], q[3], q[1]);

                        int j = 0;
                        double diffTheta = oldQuats[j].Angle - quat.Angle;
                        Vector3D diffVector = Vector3D.Subtract(oldQuats[j].Axis, quat.Axis);
                        if (diffTheta > MAX_THETA_DIFF || diffVector.Length > MAX_AXIS_DIFF)
                        {
                            if (!inventorFrameTimer.Enabled) inventorFrameTimer.Start();
                        }
                        else
                        {
                            if (inventorFrameTimer.Enabled) inventorFrameTimer.Stop();
                        }
                    }
                }
            }
        }
        
        //method for updating the inventor cam view
        private void LockedRotate(object myObject, EventArgs myEventArgs)//Vector3D a, Double theta)
        {
            Vector3D a = quat.Axis;
            double theta = quat.Angle;
            theta *= Math.PI / 180;
            theta = -theta;

            double[] camPos = RotateQuaternion(0, 0, -camDist, a, theta);
            double[] camUp = RotateQuaternion(0, -1, 0, a, theta);

            //avoid exceptions if possible
            if (inventorRunning)
            {
                try
                {
                    //avoid exceptions if possible
                    if (_invApp.ActiveView != null)
                    {
                        try
                        {
                            cam = _invApp.ActiveView.Camera;
                            tg = _invApp.TransientGeometry;
                            cam.Eye = tg.CreatePoint(camPos[0], camPos[1], camPos[2]);
                            cam.Target = tg.CreatePoint();
                            cam.UpVector = tg.CreateUnitVector(camUp[0], camUp[1], camUp[2]);
                            cam.ApplyWithoutTransition();
                        }
                        //no active view
                        catch (Exception ex)
                        {
                            MessageBox.Show("Unable to rotate Inventor Camera!\n" + ex.ToString());
                        }
                    }
                }
                //no _invApp
                catch (Exception ex)
                {
                    inventorRunning = false;
                    MessageBox.Show("Oh no! Something went wrong with Inventor!\n" + ex.ToString());
                }
            }
            else
            {
                StartInventor();
            }
        }

        //equation due to https://en.wikipedia.org/wiki/Quaternions_and_spatial_rotation, specifically:
        //https://en.wikipedia.org/wiki/Quaternions_and_spatial_rotation#Quaternion-derived_rotation_matrix
        private double[] RotateQuaternion(double x, double y, double z, Vector3D a, double theta)
        {
            double[] vect = new double[3];
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            vect[0] = x * (c + a.X * a.X * (1 - c)) + y * (a.X * a.Y * (1 - c) - a.Z * s) + z * (a.X * a.Z * (1 - c) + a.Y * s);
            vect[1] = x * (a.Y * a.X * (1 - c) + a.Z * s) + y * (c + a.Y * a.Y * (1 - c)) + z * (a.Y * a.Z * (1 - c) - a.X * s);
            vect[2] = x * (a.Z * a.X * (1 - c) - a.Y * s) + y * (a.Z * a.Y * (1 - c) + a.X * s) + z * (c + a.Z * a.Z * (1 - c));
            
            return vect;
        }

        private void Ping(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
            {
                serialPort1.Write(buff, 0, 1);
            }
        }

        //Hopefully, it's enough that the DataRecieved cuses BeginInvoke.
        //TODO: make sure there really isn't any deadlock by using Invoke for closing
        private void CloseHandler(object sender, FormClosingEventArgs e)
        {
            this.Invoke(new SimpleDelegate(CloseSequence));
            formClose = true;
        }

        private void CloseSequence()
        {
            pingTimer.Stop();
            inventorFrameTimer.Stop();
            serialPort1.Close();
            Console.WriteLine("port closed");
            synced = false;
            serialCount = 0;
        }      
        
        
        //TODO this
        private void buttonCalibrate_Click(object sender, EventArgs e)
        {

        }

        private void comboBoxPorts_SelectedIndexChanged(object sender, EventArgs e)
        {
            SERIAL_PORT = comboBoxPorts.Text;
        }

        private void buttonReconnect_Click(object sender, EventArgs e)
        {
            this.BeginInvoke(new EventHandler(delegate
            {
                serialPort1.Close();
                Console.WriteLine("port closed");
                synced = false;
                serialCount = 0;
                serialPort1.PortName = SERIAL_PORT;
                serialPort1.Open();
            }));
        }
    }
}

