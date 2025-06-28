using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.ComponentModel;
using System.ServiceProcess;
using System.Configuration.Install;
using System.Text.RegularExpressions;
using System.Threading;

namespace test.Service
{

    public class StorageDeviceMonitorService : ServiceBase
    {

        public static void Main(string[] args)
        {
            //サービスを起動する
            ServiceBase.Run(new StorageDeviceMonitorService());
        }

        Thread ProcThread;
        Queue<ProcessStartInfo> ProcExecQueue;
        string AppPath;
        private ManagementEventWatcher EventManagementEventWatcher = null;
        private Regex RegDeviceID = new Regex("DeviceID=\"(.+)\"");
        private ArrayList InstalledDevice = new ArrayList();
        private DateTime WhitelistTimestamp = DateTime.MinValue;
        private Hashtable Filename;
        private bool IsOutputDebuglog = true;

        private ArrayList BeforeDevices;

        public StorageDeviceMonitorService()
        {
            this.AutoLog = false;
            this.CanShutdown = true;
            this.CanStop = false;
            this.ServiceName = "StorageDeviceMonitor";

            AppPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Filename = new Hashtable();
            Filename["DebugLog"] = AppPath + @"\debug.txt";
            Filename["Whitelist"] = AppPath + @"\WhiteList.txt";
            Filename["Staticlist"] = AppPath + @"\InstalledDevices.txt";
            Filename["Pnputil"] = File.Exists(@"C:\Windows\System32\pnputil.exe") ? @"C:\Windows\System32\pnputil.exe" : @"C:\Windows\Sysnative\pnputil.exe";


            ProcThread = new Thread(new ThreadStart(ProcessThread));
            ProcExecQueue = new Queue<ProcessStartInfo>();
            
            BeforeDevices = GetAllDevice();

            if (File.Exists((string)Filename["Staticlist"]))
            {
                WriteLog("Read static installed list.");
                using (StreamReader sr = new StreamReader((string)Filename["Staticlist"]))
                {
                    while (!sr.EndOfStream)
                        InstalledDevice.Add(DeviceInfo.GetDeviceInfo(sr.ReadLine()));
                    sr.Close();
                }
                foreach (string item in BeforeDevices)
                {
                    if (!CheckAcceptedDevice(item))
                    {
                        WriteLog("Unauthorized device found : " + item);
                        ProcExecQueue.Enqueue(new ProcessStartInfo((string)Filename["Pnputil"], string.Format("/remove-device \"{0}\"", item)));
                    }
                }
            }
            else
            {
                WriteLog("make static installed list.");
                using (StreamWriter sw = new StreamWriter((string)Filename["Staticlist"]))
                {
                    foreach (string item in BeforeDevices)
                    {
                        sw.WriteLine(item);
                        InstalledDevice.Add(DeviceInfo.GetDeviceInfo(item));
                    }
                }
            }

            // Create WmiEvent
            WqlEventQuery query = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent");

            EventManagementEventWatcher = new ManagementEventWatcher(query);
            EventManagementEventWatcher.EventArrived += new EventArrivedEventHandler(DeviceChangeEvent);
        }


        protected override void OnStart(string[] args)
        {
            EventManagementEventWatcher.Start();
            ProcThread.Start();
        }


        protected override void OnStop()
        {
            this.RequestAdditionalTime(2000);
            EventManagementEventWatcher.Stop();

            this.ExitCode = 0;

        }

        protected override void OnShutdown()
        {
            EventManagementEventWatcher.Stop();
        }

        /// <summary>
        /// Thread for executing commands in the queue.
        /// </summary>
        void ProcessThread()
        {
            Process p = new Process();
            while (true)
            {
                if (ProcExecQueue.Count() > 0)
                {
                    try
                    {
                        Thread.Sleep(1000);
                        p.StartInfo = ProcExecQueue.Dequeue();
                        p.StartInfo.RedirectStandardError = true;
                        p.StartInfo.RedirectStandardOutput = true;
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.CreateNoWindow = true;
                        p.Start();

                        p.WaitForExit();
                        WriteLog(p.StandardError.ReadToEnd());
                        WriteLog(p.StandardOutput.ReadToEnd());
                        WriteLog("Return code: " + p.ExitCode);
                    }
                    catch (Exception e)
                    {
                        WriteLog(e);
                    }
                }
                Thread.Sleep(500);
            }
        }

        /// <summary>
        /// Event handler to detect device changes.
        /// Add unauthorized devices to the removal command queue.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DeviceChangeEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                ArrayList al = GetEventDevice();
                foreach (string item in al)
                {
                    Thread.Sleep(2000);
                    if (!CheckAcceptedDevice(item))
                    {
                        WriteLog("Unauthorized device found : " + item);
                        ProcExecQueue.Enqueue(new ProcessStartInfo((string)Filename["Pnputil"], string.Format("/remove-device \"{0}\"", item)));

                    }
                }
            }
            catch (Exception exc)
            {
                WriteLog(exc);
            }
        }

        /// <summary>
        /// Get the device where the event occurred.
        /// </summary>
        /// <returns></returns>
        private ArrayList GetEventDevice()
        {
            ArrayList alFindDevice = GetAllDevice();
            ArrayList alNewDevice = new ArrayList();

            foreach (string d in alFindDevice)
            {
                if (!BeforeDevices.Contains(d)) alNewDevice.Add(d);
            }
            BeforeDevices = alFindDevice;
            return alNewDevice;
        }

        /// <summary>
        /// Get a list of all connected storage devices.
        /// </summary>
        /// <returns></returns>
        private ArrayList GetAllDevice()
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PnPEntity WHERE Service = 'disk' OR Service = 'USBSTOR' OR Service = 'UASPStor' OR Service like '%WpdMtp%' OR Service like '%WpdPtp%'");
            ArrayList alDevices = new ArrayList();

            string deviceID;
            foreach (ManagementObject queryObj in searcher.Get())
            {
                deviceID = queryObj["DeviceID"].ToString();
                if (deviceID.StartsWith(@"USB\VID_")) alDevices.Add(deviceID.Replace(@"\\", @"\"));
            }
            return alDevices;
        }

        /// <summary>
        /// Check if the device is accepted.
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        private bool CheckAcceptedDevice(string deviceId)
        {
            bool rc = false;
            ArrayList AcceptedDeviceList = (ArrayList)InstalledDevice.Clone();

            if (File.Exists((string)Filename["Whitelist"]) && WhitelistTimestamp != File.GetLastWriteTime((string)Filename["Whitelist"]))
            {
                using (StreamReader sr = new StreamReader((string)Filename["Whitelist"]))
                {
                    AcceptedDeviceList.Add(DeviceInfo.GetRegexDeviceInfo(sr.ReadLine()));
                }
                WhitelistTimestamp = File.GetLastWriteTime((string)Filename["Whitelist"]);
            }
            foreach (DeviceInfo deviceInfo in AcceptedDeviceList)
            {
                WriteLog("Check Device: " + deviceId  + "  ==  " + deviceInfo);
                if (deviceInfo.Match(deviceId))
                {
                    rc = true;
                    break;
                }
            }
            return rc;
        }

        /// <summary>
        /// Write to the debug log.
        /// </summary>
        /// <param name="val"></param>
        void WriteLog(string val)
        {
            if (!IsOutputDebuglog) return;
            try
            {
                using (StreamWriter sw = new StreamWriter((string)Filename["DebugLog"], true))
                {
                    sw.WriteLine(string.Format("{0} : {1}",DateTime.Now.ToShortTimeString(),val));
                    sw.Close();

                }

                }
            catch (Exception)
            {
                this.EventLog.WriteEntry("An error occurred while writing to the debug log: " + val);
            }
        }
        /// <summary>
        /// Write to the debug log.
        /// </summary>
        /// <param name="val"></param>
        void WriteLog(Exception e)
        {
            WriteLog(e.ToString());
        }

        /// <summary>
        /// Device information class
        /// </summary>
        private class DeviceInfo
        {
            private DeviceInfo() { }
            private bool IsRegex = false;
            private Regex RegDeviceID = null;

            /// <summary>
            /// Create an instance for a device specified(fixed rext).
            /// </summary>
            /// <param name="device">DeviceID</param>
            /// <returns></returns>
            public static DeviceInfo GetDeviceInfo(string device)
            {
                DeviceInfo d = new DeviceInfo();
                d.DeviceID = device;
                d.IsRegex = false;
                return d;
            }
            /// <summary>
            /// Create an instance for a device specified with a wildcard (convert to regular expression).
            /// </summary>
            /// <param name="regexDevice">DeviceID with wildcard</param>
            /// <returns></returns>
            public static DeviceInfo GetRegexDeviceInfo(string regexDevice)
            {
                DeviceInfo d = new DeviceInfo();
                d.DeviceID = regexDevice;
                d.IsRegex = true;
                d.RegDeviceID = new Regex("^" + Regex.Escape(regexDevice).Replace("\\*", ".*").Replace("\\?", ".") + "$");
                return d;
            }
            /// <summary>
            /// Check if the DeviceID matches.
            /// </summary>
            /// <param name="deviceid"></param>
            /// <returns></returns>
            public bool Match(string deviceid)
            {
                if (IsRegex)
                {
                    return RegDeviceID.IsMatch(deviceid);
                }
                else
                {
                    return DeviceID.ToUpper() == deviceid.ToUpper();
                }

            }
            public string DeviceID { get; set; }
            public override string ToString() { return DeviceID; }
        }


    }


    [RunInstaller(true)]
    public class TestServiceInstaller : Installer
    {

        public TestServiceInstaller()
        {

            ServiceProcessInstaller spi = new ServiceProcessInstaller();
            spi.Username = Environment.UserName;
            spi.Account = ServiceAccount.LocalSystem;

            ServiceInstaller si = new ServiceInstaller();
            si.ServiceName = "StoradeDeviceMonitorService";
            si.DisplayName = "Storade Device Monitor Service";
            si.Description = "Monitoring connected storage device. If an unauthorized device is connected, remove (or eject) the device.";
            si.StartType = ServiceStartMode.Automatic;
            this.Installers.Add(spi);
            this.Installers.Add(si);

        }

    }

}