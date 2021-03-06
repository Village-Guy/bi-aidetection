﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json; //deserialize DeepquestAI response
//for image cutting
using SixLabors.ImageSharp;
//using SixLabors.ImageSharp.MetaData.Profiles.Exif;
//for telegram
using Telegram.Bot;
using Telegram.Bot.Types.InputFiles;
using static AITool.AITOOL;

namespace AITool
{

    public partial class Shell:Form
    {



        public Shell()
        {
            InitializeComponent();

            //---------------------------------------------------------------------------------------------------------
            // Section added by Vorlon
            //---------------------------------------------------------------------------------------------------------

            //this is to log messages from other classes to the RTF in Shell form, and to log file...
            Global.progress = new Progress<ClsMessage>(EventMessage);

            //Initialize the rich text log window writer.   You can use any 'color' name in your log text
            //for example {red}Error!{white}.  Note if you use $ for the string, you have use two brackets like this: {{red}}
            RTFLogger = new RichTextBoxEx(RTF_Log, true);
            //initialize the log and history file writers - log entries will be queued for fast file logging performance AND if the file
            //is locked for any reason, it will wait in the queue until it can be written
            //The logwriter will also rotate out log files (each day, rename as log_date.txt) and delete files older than 60 days
            LogWriter = new LogFileWriter(AppSettings.Settings.LogFileName);
            HistoryWriter = new LogFileWriter(AppSettings.Settings.HistoryFileName);

            //if log file does not exist, create it - this used to be in LOG function but doesnt need to be checked everytime log written to
            if (!System.IO.File.Exists(AppSettings.Settings.LogFileName))
            {
                //the logwriter auto creates the file if needed
                LogWriter.WriteToLog("Log format: [dd.MM.yyyy, HH:mm:ss]: Log text.", true);

            }

            //load settings
            AppSettings.Load();

            LogWriter.MaxLogFileAgeDays = AppSettings.Settings.MaxLogFileAgeDays;
            HistoryWriter.MaxLogFileAgeDays = AppSettings.Settings.MaxLogFileAgeDays;

            string AssemVer = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            Log("");
            Log("");
            lbl_version.Text = $"Version {AssemVer} built on {Global.RetrieveLinkerTimestamp()}";
            Log($"Starting {Application.ProductName} {lbl_version.Text}");
            if (AppSettings.AlreadyRunning)
            {
                Log("*** Another instance is already running *** ");
                Log(" --- Files will not be monitored from within this session ");
                Log(" --- Log tab will not display output from service instance. You will need to directly open log file for that ");
                Log(" --- Changes made here to settings will require that you stop/start the service ");
                Log(" --- You must close/reopen app to see NEW history items/detections");
            }
            if (Global.IsAdministrator())
            {
                Log("*** Running as administrator ***");
            }
            else
            {
                Log("Not running as administrator.");
            }
            //initialize blueiris info class to get camera names, clip paths, etc
            BlueIrisInfo = new BlueIris();
            if (BlueIrisInfo.IsValid)
            {
                Log($"BlueIris path is '{BlueIrisInfo.AppPath}', with {BlueIrisInfo.Cameras.Count()} cameras and {BlueIrisInfo.ClipPaths.Count()} clip folder paths configured.");
            }
            else
            {
                Log($"BlueIris not detected.");
            }
            //---------------------------------------------------------------------------------------------------------

            this.Resize += new System.EventHandler(this.Form1_Resize); //resize event to enable 'minimize to tray'

            //if camera settings folder does not exist, create it
            if (!Directory.Exists("./cameras/"))
            {
                //create folder
                DirectoryInfo di = Directory.CreateDirectory("./cameras");
                Log("./cameras/" + " dir created.");
            }

            //---------------------------------------------------------------------------
            //CAMERAS TAB

            //left list column setup
            list2.Columns.Add("Camera");

            //set left list column width segmentation (because of some bug -4 is necessary to achieve the correct width)
            list2.Columns[0].Width = list2.Width - 4;
            list2.FullRowSelect = true; //make all columns clickable

            LoadCameras(); //load camera list

            this.Opacity = 0;
            this.Show();

            //---------------------------------------------------------------------------
            //HISTORY TAB

            //left list column setup
            list1.Columns.Add("Name");
            list1.Columns.Add("Date and Time");
            list1.Columns.Add("Camera");
            list1.Columns.Add("Detections");
            list1.Columns.Add("Positions");
            list1.Columns.Add("✓");

            //set left list column width segmentation
            list1.Columns[0].Width = list1.Width * 0 / 100; //filename
            list1.Columns[1].Width = list1.Width * 47 / 100; //date
            list1.Columns[2].Width = list1.Width * 43 / 100; //cam name
            list1.Columns[3].Width = list1.Width * 0 / 100; //obj and confidences
            list1.Columns[4].Width = list1.Width * 0 / 100; // object positions of all detected objects separated by ";"
            list1.Columns[5].Width = list1.Width * 10 / 100; //checkmark if something relevant was detected or not
            list1.FullRowSelect = true; //make all columns clickable

            //check if history.csv exists, if not then create it
            if (!System.IO.File.Exists(AppSettings.Settings.HistoryFileName))
            {
                Log("ATTENTION: Creating database cameras/history.csv .");
                try
                {
                    HistoryWriter.WriteToLog("filename|date and time|camera|detections|positions of detections|success", true);

                    //using (StreamWriter sw = new StreamWriter(AppDomain.CurrentDomain.BaseDirectory + "cameras/history.csv"))
                    //{
                    //    sw.WriteLine("filename|date and time|camera|detections|positions of detections|success");
                    //}
                }
                catch
                {
                    lbl_errors.Text = "Can't create cameras/history.csv database!";
                }

            }


            //this method is slow if the database is large, so it's usually only called on startup. During runtime, DeleteListImage() is used to remove obsolete images from the history list
            CleanCSVList();

            //load entries from history.csv into history ListView
            //LoadFromCSV(); not neccessary because below, comboBox_filter_camera.SelectedIndex will call LoadFromCSV()

            splitContainer1.Panel2Collapsed = true; //collapse filter panel under left list
            comboBox_filter_camera.Items.Add("All Cameras"); //add "all cameras" entry in filter dropdown combobox
            comboBox_filter_camera.SelectedIndex = comboBox_filter_camera.FindStringExact("All Cameras"); //select all cameras entry



            //---------------------------------------------------------------------------
            //SETTINGS TAB

            //fill settings tab with stored settings 

            cmbInput.Text = AppSettings.Settings.input_path;
            cb_inputpathsubfolders.Checked = AppSettings.Settings.input_path_includesubfolders;
            cmbInput.Items.Clear();
            foreach (string pth in BlueIrisInfo.ClipPaths)
            {
                cmbInput.Items.Add(pth);

            }

            tbDeepstackUrl.Text = AppSettings.Settings.deepstack_url;
            tb_telegram_chatid.Text = String.Join(",", AppSettings.Settings.telegram_chatids);
            tb_telegram_token.Text = AppSettings.Settings.telegram_token;
            cb_log.Checked = AppSettings.Settings.log_everything;
            cb_send_errors.Checked = AppSettings.Settings.send_errors;
            cbStartWithWindows.Checked = AppSettings.Settings.startwithwindows;

            //---------------------------------------------------------------------------
            //STATS TAB
            comboBox1.Items.Add("All Cameras"); //add all cameras stats entry
            comboBox1.SelectedIndex = comboBox1.FindStringExact("All Cameras"); //select all cameras entry


            //---------------------------------------------------------------------------
            //Deepstack server TAB


            //initialize the deepstack class - it collects info from running deepstack processes, detects install location, and
            //allows for stopping and starting of its service
            DeepStackServerControl = new DeepStack(AppSettings.Settings.deepstack_adminkey, AppSettings.Settings.deepstack_apikey, AppSettings.Settings.deepstack_mode, AppSettings.Settings.deepstack_sceneapienabled, AppSettings.Settings.deepstack_faceapienabled, AppSettings.Settings.deepstack_detectionapienabled, AppSettings.Settings.deepstack_port);


            if (DeepStackServerControl.NeedsSaving)
            {
                //this may happen if the already running instance has a different port, etc, so we update the config
                SaveDeepStackTab();
            }
            LoadDeepStackTab(true);

            //set httpclient timeout:
            client.Timeout = TimeSpan.FromSeconds(AppSettings.Settings.HTTPClientTimeoutSeconds);

            UpdateWatchers();

            //Start the thread that watches for the file queue
            Task.Run(ImageQueueLoop);


            this.Opacity = 1;

            Log("APP START complete.");
        }





        void EventMessage(ClsMessage msg)
        {
            //output messages from the deepstack, blueiris, etc class to the text log window and log file
            if (msg.MessageType == MessageType.LogEntry)
            {
                Log(msg.Description, "");
            }
            else if (msg.MessageType == MessageType.CreateHistoryItem)
            {
                JsonSerializerSettings jset = new JsonSerializerSettings { };
                jset.TypeNameHandling = TypeNameHandling.All;
                jset.PreserveReferencesHandling = PreserveReferencesHandling.Objects;
                ClsHistoryItem hist = JsonConvert.DeserializeObject<ClsHistoryItem>(msg.JSONPayload, jset);
                CreateListItem(hist);
            }
            else if (msg.MessageType == MessageType.DeleteHistoryItem)
            {
                DeleteListItem(msg.Description);
            }
            else if (msg.MessageType == MessageType.ImageAddedToQueue)
            {
                UpdateQueueLabel();
            }
            else if (msg.MessageType == MessageType.BeginProcessImage)
            {
                BeginProcessImage(msg.Description);
            }
            else if (msg.MessageType == MessageType.EndProcessImage)
            {
                EndProcessImage(msg.Description);
            }
            else if (msg.MessageType == MessageType.UpdateLabel)
            {
                string lblcontrolname = (string)Global.SetJSONString<object>(msg.JSONPayload);

                if (!string.IsNullOrWhiteSpace(lblcontrolname))
                {
                    Label lbl = null;
                    try
                    {
                        lbl = this.Controls.Find(lblcontrolname, true).FirstOrDefault() as Label;
                    }
                    catch (Exception ex)
                    {

                        Log($"Error: Could not find label '{lblcontrolname}': {Global.ExMsg(ex)}");
                    }

                    if (lbl != null)
                    {
                        if (this.Visible)
                        {
                            MethodInvoker LabelUpdate = delegate
                            {
                                lbl.Show();
                                lbl.Text = msg.Description;
                            };
                            //getting error here when called too early - had to check if Visible or not -Vorlon
                            Invoke(LabelUpdate);

                        }
                    }

                }
                else
                {
                    Log($"Error: No label name passed - '{msg.Description}'");

                }


            }
            else
            {
                Log($"Error: Unhandled message type '{msg.MessageType}'");
            }
        }
        //----------------------------------------------------------------------------------------------------------
        //CORE
        //----------------------------------------------------------------------------------------------------------



        //save how many times an error happened
        public void IncrementErrorCounter()
        {
            errors++;
            try
            {
                if (this.Visible)
                {
                    MethodInvoker LabelUpdate = delegate
                    {
                        lbl_errors.Show();
                        lbl_errors.Text = $"{errors.ToString()} error(s) occurred. Click to open Log."; //update error counter label
                    };
                    //getting error here when called too early - had to check if Visible or not -Vorlon
                    Invoke(LabelUpdate);

                }

            }
            catch (Exception)
            {

            }
        }

        //add text to log
        public async void Log(string text, [CallerMemberName] string memberName = null)
        {

            try
            {

                //get current date and time

                string time = DateTime.Now.ToString("dd.MM.yyyy, HH:mm:ss");
                string rtftime = DateTime.Now.ToString("dHH:mm:ss");  //no need for date in log tab
                string ModName = "";
                if (memberName == ".ctor")
                    memberName = "Constructor";

                if (AppSettings.Settings.log_everything == true || AppSettings.Settings.deepstack_debug)
                {
                    time = DateTime.Now.ToString("dd.MM.yyyy, HH:mm:ss.fff");
                    rtftime = DateTime.Now.ToString("HH:mm:ss.fff");
                    if (memberName != null && !string.IsNullOrEmpty(memberName))
                        ModName = memberName.PadLeft(24) + "> ";

                    //when the global logger reports back to the progress logger we cant use CallerMemberName, so extract the member name from text

                    int gg = text.IndexOf(">> ");

                    if (gg > 0 && gg <= 24)
                    {
                        string modfromglobal = Global.GetWordBetween(text, "", ">> ");
                        if (!string.IsNullOrEmpty(modfromglobal))
                        {
                            ModName = modfromglobal.PadLeft(24) + "> ";
                            text = Global.GetWordBetween(text, ">> ", "");
                        }

                    }
                }

                //check for messages coming from deepstack processes and kill them if we didnt ask for debugging messages
                if (!AppSettings.Settings.deepstack_debug)
                {
                    if (text.ToLower().Contains("redis-server.exe>") || text.ToLower().Contains("python.exe>"))
                    {
                        return;
                    }
                }

                //make the error and warning detection case insensitive:
                bool HasError = (text.IndexOf("error", StringComparison.InvariantCultureIgnoreCase) > -1) || (text.IndexOf("exception", StringComparison.InvariantCultureIgnoreCase) > -1) || (text.IndexOf("fail", StringComparison.InvariantCultureIgnoreCase) > -1);
                bool HasWarning = (text.IndexOf("warning", StringComparison.InvariantCultureIgnoreCase) > -1);
                bool IsDeepStackMsg = (memberName.IndexOf("deepstack", StringComparison.InvariantCultureIgnoreCase) > -1) || (text.IndexOf("deepstack", StringComparison.InvariantCultureIgnoreCase) > -1) || (ModName.IndexOf("deepstack", StringComparison.InvariantCultureIgnoreCase) > -1);
                string RTFText = "";

                //set the color for RTF text window:
                if (HasError)
                {
                    RTFText = $"{{gray}}[{rtftime}]: {ModName}{{red}}{text}";
                }
                else if (HasWarning)
                {
                    RTFText = $"{{gray}}[{rtftime}]: {ModName}{{mediumorchid}}{text}";
                }
                else if (IsDeepStackMsg)
                {
                    RTFText = $"{{gray}}[{rtftime}]: {ModName}{{lime}}{text}";
                }
                else
                {
                    RTFText = $"{{gray}}[{rtftime}]: {ModName}{{white}}{text}";
                }

                //get rid of any common color coding before logging to file or console
                text = text.Replace("{yellow}", "").Replace("{red}", "").Replace("{white}", "").Replace("{orange}", "").Replace("{lime}", "").Replace("{orange}", "mediumorchid");

                //if log everything is disabled and the text is neither an ERROR, nor a WARNING: write only to console and ABORT
                if (AppSettings.Settings.log_everything == false && !HasError && !HasWarning)
                {
                    //Creates a lot of extra text in immediate window while debugging, disabling -Vorlon
                    //text += "Enabling \'Log everything\' might give more information.";
                    Console.WriteLine($"[{rtftime}]: {ModName}{text}");

                    return;
                }



                //add text to log
                try
                {
                    RTFLogger.LogToRTF(RTFText);
                    LogWriter.WriteToLog($"[{time}]:  {ModName}{text}", HasError);

                }
                catch
                {
                    MethodInvoker LabelUpdate = delegate { lbl_errors.Text = "Can't write to log.txt file!"; };
                    Invoke(LabelUpdate);
                }

                if (AppSettings.Settings.send_errors == true && (HasError || HasWarning))
                {
                    await TelegramText($"[{time}]: {text}"); //upload text to Telegram
                }



                //add log text to console
                Console.WriteLine($"[{rtftime}]: {ModName}{text}");

                //increment error counter
                if (HasError || HasWarning)
                {
                    IncrementErrorCounter();
                }

            }
            catch (Exception ex)
            {

                Console.WriteLine("Error: In LOG, got: " + ex.Message);
            }

        }



        //----------------------------------------------------------------------------------------------------------
        //GUI
        //----------------------------------------------------------------------------------------------------------

        //minimize to tray
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon.Visible = true;
            }
            else
            {
                ResizeListViews();
            }
        }

        //open from tray
        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
        }

        //open Log when clicking or error message
        private void lbl_errors_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists(AppSettings.Settings.LogFileName))
            {
                System.Diagnostics.Process.Start(AppSettings.Settings.LogFileName);
                lbl_errors.Text = "";
            }
            else
            {
                MessageBox.Show("log missing");
            }

        }

        //adapt list views (history tab and cameras tab) to window size while considering scrollbar influence
        private void ResizeListViews()
        {
            //suspend layout of most complex tablelayout elements (gives a few milliseconds)
            tableLayoutPanel7.SuspendLayout();
            tableLayoutPanel8.SuspendLayout();
            //tableLayoutPanel9.SuspendLayout();

            //variable storing list1 effective width
            int width = list1.Width;

            //subtract vertical scrollbar width if scrollbar is shown (scrollbar is shown when there are more items(including the header row) than fit in the visible space of the list) 
            try
            {
                if (list1.Items.Count > 0 && list1.Height <= (list1.GetItemRect(0).Height * (list1.Items.Count + 1)))
                {
                    width -= SystemInformation.VerticalScrollBarWidth;
                }
            }
            catch
            {
                Log("ERROR in ReziseListViews(), checking if scrollbar is shown and subtracting scrollbar width failed.");
            }

            //fix an exception where form_resize calls this function too early:
            if (list1.Columns.Count > 0)
            {
                if (width > 350) // if the list is wider than 350px, aditionally show the 'detections' column and mainly grow this column
                {
                    //set left list column width segmentation
                    list1.Columns[0].Width = width * 0 / 100; //filename
                    list1.Columns[1].Width = 120 + (width - 350) * 25 / 1000; //date
                    list1.Columns[2].Width = 120 + (width - 350) * 25 / 1000; //cam name
                    list1.Columns[3].Width = 80 + (width - 350) * 95 / 100; //obj and confidences
                    list1.Columns[4].Width = width * 0 / 100; // object positions of all detected objects separated by ";"
                    list1.Columns[5].Width = 30; //checkmark if something relevant detected or not

                }
                else //if the form is smaller than 350px in width, don't show the detections column
                {
                    //set left list column width segmentation
                    list1.Columns[0].Width = width * 0 / 100; //filename
                    list1.Columns[1].Width = width * 47 / 100; //date
                    list1.Columns[2].Width = width * 43 / 100; //cam name
                    list1.Columns[3].Width = width * 0 / 100; //obj and confidences
                    list1.Columns[4].Width = width * 0 / 100; // object positions of all detected objects separated by ";"
                    list1.Columns[5].Width = width * 10 / 100; //checkmark if something relevant detected or not
                }

            }

            if (list2.Columns.Count > 0)
                list2.Columns[0].Width = list2.Width - 4; //resize camera list column

            //resume layout again
            tableLayoutPanel7.ResumeLayout();
            tableLayoutPanel8.ResumeLayout();
            //tableLayoutPanel9.ResumeLayout();
        }

        //add last trigger time to label on Overview page
        //private async Task LastTriggerInfo(Camera cam)
        //{
        //    Global_GUI.InvokeIFRequired(this.lbl_info, async () =>
        //    {
        //        string text1 = $"{cam.name} last triggered at {cam.last_trigger_time}. Sleeping for {cam.cooldown_time / 2} minutes."; //write last trigger time to label on Overview page
        //        lbl_info.Text = text1;

        //        int time = 30 * Convert.ToInt32(1000 * cam.cooldown_time);
        //        await Task.Delay(time); // wait while the analysis is sleeping for this camera
        //        if (lbl_info.Text == text1)
        //        {
        //            lbl_info.Text = $"{cam.name} last triggered at {cam.last_trigger_time}."; //Remove "sleeping for ..."
        //        }

        //    });

        //}


        //EVENTS:

        //event: mouse click on tab control
        private void tabControl1_MouseDown(object sender, MouseEventArgs e)
        {
            ResizeListViews();
        }

        //event: another tab selected (Only load certain things in tabs if they are actually open)
        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedIndex == 1)
            {
                UpdatePieChart(); UpdateTimeline(); UpdateConfidenceChart();
            }
            else if (tabControl1.SelectedIndex == 2)
            {
                //CleanCSVList(); //removed to load the history list faster
            }
            else if (tabControl1.SelectedTab == tabControl1.TabPages["tabDeepStack"])
            {
                LoadDeepStackTab(true);
            }
            else if (tabControl1.SelectedTab == tabControl1.TabPages["tabLog"])
            {
                //scroll to bottom, only when tab is active for better performance 

                Global_GUI.InvokeIFRequired(this.RTF_Log, () =>
                {
                    if (Chk_AutoScroll.Checked)
                    {
                        this.RTF_Log.SelectionStart = this.RTF_Log.Text.Length;
                        this.RTF_Log.ScrollToCaret();
                    }
                });
            }

        }


        //----------------------------------------------------------------------------------------------------------
        //STATS TAB
        //----------------------------------------------------------------------------------------------------------

        //other camera in combobox selected, display according PieChart
        private void comboBox1_SelectedIndexChanged_1(object sender, EventArgs e)
        {
            if (tabControl1.SelectedIndex == 1)
            {

                UpdatePieChart(); UpdateTimeline(); UpdateConfidenceChart();
            }
        }

        //update pie chart
        public void UpdatePieChart()
        {
            int alerts = 0;
            int irrelevantalerts = 0;
            int falsealerts = 0;

            if (comboBox1.Text == "All Cameras")
            {
                foreach (Camera cam in AppSettings.Settings.CameraList)
                {
                    alerts += cam.stats_alerts;
                    irrelevantalerts += cam.stats_irrelevant_alerts;
                    falsealerts += cam.stats_false_alerts;
                }
            }
            else
            {

                Camera cam = AITOOL.GetCamera(comboBox1.Text);  //int i = AppSettings.Settings.CameraList.FindIndex(x => x.name.ToLower().Trim() == comboBox1.Text.ToLower().Trim());
                if (cam != null)
                {
                    alerts = cam.stats_alerts;
                    irrelevantalerts = cam.stats_irrelevant_alerts;
                    falsealerts = cam.stats_false_alerts;
                }
                else
                {
                    alerts = 0;
                    irrelevantalerts = 0;
                    falsealerts = 0;
                    Log($"Error: Could not match combobox dropdown '{comboBox1.Text}' to a known camera name?");
                }
            }

            chart1.Series[0].Points.Clear();

            chart1.Series[0].LegendText = "#VALY #VALX";
            chart1.Series[0]["PieLabelStyle"] = "Disabled";

            int index = -1;

            //show Alerts label
            index = chart1.Series[0].Points.AddXY("Alerts", alerts);
            chart1.Series[0].Points[index].Color = System.Drawing.Color.Green;

            //show irrelevant Alerts label
            index = chart1.Series[0].Points.AddXY("irrelevant Alerts", irrelevantalerts);
            chart1.Series[0].Points[index].Color = System.Drawing.Color.Orange;

            //show false Alerts label
            index = chart1.Series[0].Points.AddXY("false Alerts", falsealerts);
            chart1.Series[0].Points[index].Color = System.Drawing.Color.OrangeRed;
        }

        //update timeline
        public async void UpdateTimeline()
        {
            Log("Loading time line from cameras/history.csv ...");

            //clear previous values
            timeline.Series[0].Points.Clear();
            timeline.Series[1].Points.Clear();
            timeline.Series[2].Points.Clear();
            timeline.Series[3].Points.Clear();

            List<string> result = new List<string>(); //List that later on will be containing all lines of the csv file

            Stopwatch SW = Stopwatch.StartNew();

            try
            {
                bool Success = await Global.WaitForFileAccessAsync(AppSettings.Settings.HistoryFileName, FileSystemRights.Read, FileShare.ReadWrite);

                if (Success)
                {
                    if (comboBox1.Text.Trim() == "All Cameras") //all cameras selected
                    {
                        //load all lines except the first line
                        foreach (string line in System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName).Skip(1))
                        {
                            result.Add(line);
                        }
                    }
                    else //camera selection
                    {
                        string cameraname = comboBox1.Text.Trim();

                        //load all lines from the history.csv except the first line into List (the first line is the table heading and not an alert entry)
                        foreach (string line in System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName).Skip(1))
                        {
                            if (line.Split('|')[2] == cameraname)
                            {
                                result.Add(line);
                            }
                        }
                    }

                    //every int represents the number of ai calls in successive half hours (p.e. relevant[0] is 0:00-0:30 o'clock, relevant[1] is 0:30-1:00 o'clock) 
                    int[] all = new int[48];
                    int[] falses = new int[48];
                    int[] irrelevant = new int[48];
                    int[] relevant = new int[48];

                    //fill arrays with amount of calls/half hour
                    foreach (var val in result)
                    {               //example of time column entry: 23.08.19, 18:31:09
                                    //get hour
                        string hourstring = val.Split('|')[1].Split(',')[1].Split(':')[0];
                        int hour;
                        Int32.TryParse(hourstring, out hour);

                        //get minute
                        string minutestring = val.Split('|')[1].Split(',')[1].Split(':')[1];
                        int minute;
                        Int32.TryParse(minutestring, out minute);

                        int halfhour; //stores the half hour in which the alert occured

                        //add +1 to counter for corresponding half-hour
                        if (minute > 30) //if alert occurred after half o clock
                        {
                            halfhour = hour * 2 + 1;
                        }
                        else //if alert occured before half o clock
                        {
                            halfhour = hour * 2;
                        }

                        //if detection was successful
                        if (val.Split('|')[5] == "true")
                        {
                            relevant[halfhour]++;
                        }
                        //if it was a false alert
                        else if (val.Split('|')[3] == "false alert")
                        {
                            falses[halfhour]++;
                        }
                        //if something irrelevant was detected
                        else
                        {
                            irrelevant[halfhour]++;
                        }

                        all[halfhour]++;
                    }

                    //add to graph "all":

                    /*the graph will have a gap at the end and at the beginning if we don'f specify a value
                    * with an x value outside the visible area at the end and before the first visible point. 
                    * So the first point is at -0.25 and has the value of the last visible point and the 
                    * last point is at 24.25 and has the value of the first visible point. */

                    timeline.Series[0].Points.AddXY(-0.25, all[47]); // beginning point with value of last visible point

                    //and now add all visible points 
                    double x = 0.25;
                    foreach (int halfhour in all)
                    {
                        int index = timeline.Series[0].Points.AddXY(x, halfhour);
                        x = x + 0.5;
                    }

                    timeline.Series[0].Points.AddXY(24.25, all[0]); // finally add last point with value of first visible point

                    //add to graph "falses":

                    timeline.Series[1].Points.AddXY(-0.25, falses[47]); // beginning point with value of last visible point
                                                                        //and now add all visible points 
                    x = 0.25;
                    foreach (int halfhour in falses)
                    {
                        int index = timeline.Series[1].Points.AddXY(x, halfhour);
                        x = x + 0.5;
                    }
                    timeline.Series[1].Points.AddXY(24.25, falses[0]); // finally add last point with value of first visible point

                    //add to graph "irrelevant":

                    timeline.Series[2].Points.AddXY(-0.25, irrelevant[47]); // beginning point with value of last visible point
                                                                            //and now add all visible points 
                    x = 0.25;
                    foreach (int halfhour in irrelevant)
                    {
                        int index = timeline.Series[2].Points.AddXY(x, halfhour);
                        x = x + 0.5;
                    }
                    timeline.Series[2].Points.AddXY(24.25, irrelevant[0]); // finally add last point with value of first visible point

                    //add to graph "relevant":

                    timeline.Series[3].Points.AddXY(-0.25, relevant[47]); // beginning point with value of last visible point
                                                                          //and now add all visible points 
                    x = 0.25;
                    foreach (int halfhour in relevant)
                    {
                        int index = timeline.Series[3].Points.AddXY(x, halfhour);
                        x = x + 0.5;
                    }
                    timeline.Series[3].Points.AddXY(24.25, relevant[0]); // finally add last point with value of first visible point


                }
                else
                {
                    Log($"Error: Could not gain access to history file for {SW.ElapsedMilliseconds}ms - {AppSettings.Settings.HistoryFileName}");

                }

            }
            catch (Exception ex)
            {

                Log("Error: " + Global.ExMsg(ex));
            }




        }

        //update confidence_frequency chart
        public async void UpdateConfidenceChart()
        {
            Log("Loading confidence-frequency chart from cameras/history.csv ...");

            //clear previous values
            chart_confidence.Series[0].Points.Clear();
            chart_confidence.Series[1].Points.Clear();

            List<string> result = new List<string>(); //List that later on will be containing all lines of the csv file

            Stopwatch SW = Stopwatch.StartNew();

            try
            {
                bool Success = await Global.WaitForFileAccessAsync(AppSettings.Settings.HistoryFileName, FileSystemRights.Read, FileShare.ReadWrite);

                if (Success)
                {
                    if (comboBox1.Text == "All Cameras") //all cameras selected
                    {
                        //load all lines except the first line
                        foreach (string line in System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName).Skip(1))
                        {
                            result.Add(line);
                        }
                    }
                    else //camera selection
                    {
                        string cameraname = comboBox1.Text.Trim();

                        //load all lines from the history.csv except the first line into List (the first line is the table heading and not an alert entry)
                        foreach (string line in System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName).Skip(1))
                        {
                            if (line.Split('|')[2] == cameraname)
                            {
                                result.Add(line);
                            }
                        }
                    }


                    //this array stores the Absolute frequencies of all possible confidence values (0%-100%)
                    int[] green_values = new int[101];
                    int[] orange_values = new int[101];

                    //fill array with frequencies
                    foreach (var line in result)
                    {
                        //example of detections column entry: "person (41%); person (97%);" or "masked: person (41%); person (97%);"
                        string detections_column = line.Split('|')[3];
                        if (detections_column.Contains(':'))
                        {
                            detections_column = detections_column.Split(':')[1];

                            string[] detections = detections_column.Split(';');

                            //write the confidence of every detection into the green_values string
                            foreach (string detection in detections)
                            {
                                if (detection.Contains('%'))
                                {
                                    //example: -> "person (41%)"
                                    Int32.TryParse(detection.Split('(')[1].Split('%')[0], out int x_value); //example: -> "41"
                                    orange_values[x_value]++;
                                }
                            }
                        }
                        else
                        {
                            string[] detections = detections_column.Split(';');

                            //write the confidence of every detection into the green_values string
                            foreach (string detection in detections)
                            {
                                if (detection.Contains('%'))
                                {
                                    //example: -> "person (41%)"
                                    Int32.TryParse(detection.Split('(')[1].Split('%')[0], out int x_value); //example: -> "41"
                                    green_values[x_value]++;
                                }
                            }
                        }
                    }


                    //write green series in chart
                    int i = 0;
                    foreach (int y_value in green_values)
                    {
                        chart_confidence.Series[1].Points.AddXY(i, y_value);
                        i++;
                    }

                    //write orange series in chart
                    i = 0;
                    foreach (int y_value in orange_values)
                    {
                        chart_confidence.Series[0].Points.AddXY(i, y_value);
                        i++;
                    }


                }
                else
                {
                    Log($"Error: Could not gain access to history file for {SW.ElapsedMilliseconds}ms - {AppSettings.Settings.HistoryFileName}");

                }

            }
            catch (Exception ex)
            {

                Log("Error: " + Global.ExMsg(ex));
            }


        }


        //----------------------------------------------------------------------------------------------------------
        //HISTORY TAB
        //----------------------------------------------------------------------------------------------------------

        // load images from input_path to left list
        /*public void LoadList()
        {
            list1.Items.Clear();
            try
            {
                string[] files = Directory.GetFiles(input_path, $"*.jpg");

                foreach (string file in files)
                {

                    string fileName = Path.GetFileName(file);
                    ListViewItem item = new ListViewItem(new string[] { fileName, "content" });
                    item.Tag = file;

                    list1.Items.Add(item);

                }
            }
            catch
            {
                MessageBox.Show("Can't find the input directory, please check it.");
            }
            if (list1.Items.Count > 0)
            {
                list1.Items[0].Selected = true; //select first image
            }
        }*/

        //show or hide the privacy mask overlay
        //TODO:refactor
        private void showHideMask()
        {
            if (cb_showMask.Checked == true) //show overlay
            {
                Log("Show mask toggled.");
                if (list1.SelectedItems.Count > 0)
                {
                    if (System.IO.File.Exists("./cameras/" + list1.SelectedItems[0].SubItems[2].Text + ".png")) //check if privacy mask file exists
                    {
                        using (var img = new Bitmap("./cameras/" + list1.SelectedItems[0].SubItems[2].Text + ".png"))
                        {
                            pictureBox1.Image = new Bitmap(img); //load mask as overlay
                        }
                    }
                    else if (System.IO.File.Exists("./cameras/" + list1.SelectedItems[0].SubItems[2].Text + ".bmp")) //check if privacy mask file exists
                    {
                        using (var img = new Bitmap("./cameras/" + list1.SelectedItems[0].SubItems[2].Text + ".bmp"))
                        {
                            pictureBox1.Image = new Bitmap(img); //load mask as overlay
                        }
                    }
                    else
                    {
                        pictureBox1.Image = null; //if file does not exist, empty mask overlay (from possible overlays of previous images)
                    }

                }
            }
            else //if showmask toggle-button is not checked, hide the mask overlay
            {
                pictureBox1.Image = null;
            }

        }

        //show rectangle overlay
        private void showObject(PaintEventArgs e, System.Drawing.Color color, int _xmin, int _ymin, int _xmax, int _ymax, string text)
        {
            try
            {
                if ((list1.SelectedItems.Count > 0) && (pictureBox1 != null) && (pictureBox1.BackgroundImage != null))
                {
                    //1. get the padding between the image and the picturebox border

                    //get dimensions of the image and the picturebox
                    float imgWidth = pictureBox1.BackgroundImage.Width;
                    float imgHeight = pictureBox1.BackgroundImage.Height;
                    float boxWidth = pictureBox1.Width;
                    float boxHeight = pictureBox1.Height;

                    //these variables store the padding between image border and picturebox border
                    int absX = 0;
                    int absY = 0;

                    //because the sizemode of the picturebox is set to 'zoom', the image is scaled down
                    float scale = 1;


                    //Comparing the aspect ratio of both the control and the image itself.
                    if (imgWidth / imgHeight > boxWidth / boxHeight) //if the image is p.e. 16:9 and the picturebox is 4:3
                    {
                        scale = boxWidth / imgWidth; //get scale factor
                        absY = (int)(boxHeight - scale * imgHeight) / 2; //padding on top and below the image
                    }
                    else //if the image is p.e. 4:3 and the picturebox is widescreen 16:9
                    {
                        scale = boxHeight / imgHeight; //get scale factor
                        absX = (int)(boxWidth - scale * imgWidth) / 2; //padding left and right of the image
                    }

                    //2. inputted position values are for the original image size. As the image is probably smaller in the picturebox, the positions must be adapted. 
                    int xmin = (int)(scale * _xmin) + absX;
                    int xmax = (int)(scale * _xmax) + absX;
                    int ymin = (int)(scale * _ymin) + absY;
                    int ymax = (int)(scale * _ymax) + absY;


                    //3. paint rectangle
                    System.Drawing.Rectangle rect = new System.Drawing.Rectangle(xmin, ymin, xmax - xmin, ymax - ymin);
                    using (Pen pen = new Pen(color, 2))
                    {
                        e.Graphics.DrawRectangle(pen, rect); //draw rectangle
                    }

                    //object name text below rectangle
                    rect = new System.Drawing.Rectangle(xmin - 1, ymax, (int)boxWidth, (int)boxHeight); //sets bounding box for drawn text


                    Brush brush = new SolidBrush(color); //sets background rectangle color

                    System.Drawing.SizeF size = e.Graphics.MeasureString(text, new Font("Segoe UI Semibold", 10)); //finds size of text to draw the background rectangle
                    e.Graphics.FillRectangle(brush, xmin - 1, ymax, size.Width, size.Height); //draw grey background rectangle for detection text
                    e.Graphics.DrawString(text, new Font("Segoe UI Semibold", 10), Brushes.Black, rect); //draw detection text



                }

            }
            catch (Exception ex)
            {

                Log("Error: " + Global.ExMsg(ex));
            }
        }

        //load object rectangle overlays
        //TODO: refactor detections
        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            if (cb_showObjects.Checked && list1.SelectedItems.Count > 0) //if checkbox button is enabled
            {
                //Log("Loading object rectangles...");
                int countr = list1.SelectedItems[0].SubItems[4].Text.Split(';').Count();

                System.Drawing.Color color = new System.Drawing.Color();
                string detections = list1.SelectedItems[0].SubItems[3].Text;
                if (detections.Contains("irrelevant") || detections.Contains("masked") || detections.Contains("confidence"))
                {
                    color = System.Drawing.Color.FromArgb(AppSettings.Settings.RectIrrelevantColorAlpha, AppSettings.Settings.RectIrrelevantColor);
                    detections = detections.Split(':')[1]; //removes the "1x masked, 3x irrelevant:" before the actual detection, otherwise this would be displayed in the detection tags
                }
                else
                {
                    color = System.Drawing.Color.FromArgb(AppSettings.Settings.RectRelevantColorAlpha, AppSettings.Settings.RectRelevantColor);
                }

                //display a rectangle around each relevant object
                for (int i = 0; i < countr - 1; i++)
                {
                    string[] detectionsArray = detections.Split(';');//creates array of detected objects, used for adding text overlay
                    //load 'xmin,ymin,xmax,ymax' from third column into a string
                    string position = list1.SelectedItems[0].SubItems[4].Text.Split(';')[i];

                    //store xmin, ymin, xmax, ymax in separate variables
                    Int32.TryParse(position.Split(',')[0], out int xmin);
                    Int32.TryParse(position.Split(',')[1], out int ymin);
                    Int32.TryParse(position.Split(',')[2], out int xmax);
                    Int32.TryParse(position.Split(',')[3], out int ymax);

                    //Log($"{i} - {xmin}, {ymin}, {xmax},  {ymax}");

                    showObject(e, color, xmin, ymin, xmax, ymax, detectionsArray[i]); //call rectangle drawing method, calls appropriate detection text

                    //Log("Done.");
                }
            }
        }

        // add new entry in left list
        public void CreateListItem(ClsHistoryItem hist)  //string filename, string date, string camera, string objects_and_confidence, string object_positions
        {
            string success = "false";
            if (hist.Detections.Contains("%") && !hist.Detections.Contains(':'))
                success = "true";

            MethodInvoker LabelUpdate = delegate
            {
                if (checkListFilters(hist.Camera, success, hist.Detections)) //only show the entry in the history list if no filter applies
                {
                    ListViewItem item;
                    if (success == "true")
                    {
                        item = new ListViewItem(new string[] { hist.Filename, hist.Date, hist.Camera, hist.Detections, hist.Positions, "✓" });
                        item.ForeColor = System.Drawing.Color.Green;
                    }
                    else
                    {
                        item = new ListViewItem(new string[] { hist.Filename, hist.Date, hist.Camera, hist.Detections, hist.Positions, "X" });
                    }

                    //add the FULL path to the item tag so we dont need to add a column
                    //** no need, saving full filename in list anyway
                    //item.Tag = filename;

                    list1.Items.Insert(0, item);

                    ResizeListViews();
                }

                //update history CSV
                string line = $"{hist.Filename}|{hist.Date}|{hist.Camera}|{hist.Detections}|{hist.Positions}|{success}";
                HistoryWriter.WriteToLog(line);

            };
            Invoke(LabelUpdate);


        }

        //remove entry from left list
        public void DeleteListItem(string filename)
        {

            using (Global_GUI.CursorWait cw = new Global_GUI.CursorWait(false, false))
            {
                Stopwatch SW = Stopwatch.StartNew();

                MethodInvoker LabelUpdate = async delegate
                {
                    //bool isfullpath = filename.Contains("\\");
                    //string justfile = filename;
                    //if (isfullpath)
                    //{
                    //    justfile = Path.GetFileName(filename);
                    //}

                    ListViewItem listviewitem = new ListViewItem();
                    for (int i = 0; i < list1.Items.Count; i++)
                    {
                        listviewitem = list1.Items[i];
                        if (listviewitem.Text.ToLower().Contains(filename.ToLower()))
                        {
                            list1.Items.Remove(listviewitem);
                            break;
                        }
                    }

                    ResizeListViews();

                    //remove entry from history csv
                    try
                    {
                        bool Success = await Global.WaitForFileAccessAsync(AppSettings.Settings.HistoryFileName, FileSystemRights.Read, FileShare.ReadWrite);
                        if (Success)
                        {
                            string[] oldLines = System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName);
                            string[] newLines = oldLines.Where(line => !line.Split('|')[0].ToLower().Contains(filename.ToLower())).ToArray();
                            if (oldLines.Count() != newLines.Count())
                            {
                                Success = await Global.WaitForFileAccessAsync(AppSettings.Settings.HistoryFileName, FileSystemRights.Read, FileShare.ReadWrite);
                                if (Success)
                                {
                                    System.IO.File.WriteAllLines(AppSettings.Settings.HistoryFileName, newLines);
                                }
                                else
                                {
                                    Log($"Error: Could not gain access to history file for {SW.ElapsedMilliseconds}ms - {AppSettings.Settings.HistoryFileName}");

                                }
                            }
                        }
                        else
                        {
                            Log($"Error: Could not gain access to history file for {SW.ElapsedMilliseconds}ms - {AppSettings.Settings.HistoryFileName}");

                        }

                    }
                    catch (Exception ex)
                    {
                        Log("ERROR: Can't write to cameras/history.csv: " + Global.ExMsg(ex));
                    }

                };

                Invoke(LabelUpdate);



                //try to get a better feel how much time this function consumes - Vorlon
                //Log($"Removed alert image '{filename}' from history list and from cameras/history.csv in {{yellow}}{SW.ElapsedMilliseconds}ms{{white}} ({list1.Items.Count} list items)");

            }

        }

        //remove all obsolete entries (associated image does not exist anymore) from the history.csv 
        public void CleanCSVList()
        {

            if (AppSettings.AlreadyRunning)
            {
                Log($"Skipping clean of history.csv, instance already running.");
                return;
            }

            using (Global_GUI.CursorWait cw = new Global_GUI.CursorWait(false, false))
            {

                Log($"Cleaning cameras/history.csv if necessary...");

                Stopwatch SW = Stopwatch.StartNew();
                Int32 oldcsvlines = 0;
                Int32 newcsvlines = 0;

                MethodInvoker LabelUpdate = async delegate
                {
                    try
                    {
                        if (System.IO.File.Exists(AppSettings.Settings.HistoryFileName))
                        {

                            bool Success = await Global.WaitForFileAccessAsync(AppSettings.Settings.HistoryFileName, FileSystemRights.Read, FileShare.ReadWrite);

                            if (Success)
                            {
                                string[] oldLines = System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName); //old history.csv
                                oldcsvlines = oldLines.Count();

                                List<string> newLines = new List<string>(); //new history.csv
                                newLines.Add(oldLines[0]); // add title line from old to new history.csv

                                foreach (string line in oldLines.Skip(1)) //check for every line except title line if associated image still exists in input folder 
                                {
                                    if (System.IO.File.Exists(line.Split('|')[0]))
                                    {
                                        newLines.Add(line);
                                    }
                                }
                                newcsvlines = newLines.Count;

                                System.IO.File.WriteAllLines(AppSettings.Settings.HistoryFileName, newLines); //write new history.csv
                            }
                            else
                            {
                                Log($"Error: Could not gain access to history file for {SW.ElapsedMilliseconds}ms - {AppSettings.Settings.HistoryFileName}");
                            }

                        }
                        else
                        {
                            Log("File does not exist yet: cameras/history.csv");
                        }
                    }
                    catch
                    {
                        Log("ERROR: Can't clean the cameras/history.csv!");
                    }

                };
                Invoke(LabelUpdate);

                //try to get a better feel how much time this function consumes - Vorlon
                Log($"...Cleaned list in {{yellow}}{SW.ElapsedMilliseconds}ms{{white}}, {newcsvlines} CVS lines, removed {oldcsvlines - newcsvlines}");

            }

        }

        //load stored entries in history CSV into history ListView
        private async Task LoadFromCSVAsync()
        {
            try
            {
                using (Global_GUI.CursorWait cw = new Global_GUI.CursorWait(false, false))
                {

                    if (System.IO.File.Exists(AppSettings.Settings.HistoryFileName))
                    {
                        Log("Loading history list from cameras/history.csv ...");

                        Stopwatch SW = Stopwatch.StartNew();

                        //delete obsolete entries from history.csv
                        //CleanCSVList(); //removed to load the history list faster

                        List<string> result = new List<string>(); //List that later on will be containing all lines of the csv file

                        bool Success = await Global.WaitForFileAccessAsync(AppSettings.Settings.HistoryFileName);

                        if (Success)
                        {
                            //load all lines except the first line into List (the first line is the table heading and not an alert entry)
                            foreach (string line in System.IO.File.ReadAllLines(AppSettings.Settings.HistoryFileName).Skip(1))
                            {
                                result.Add(line);
                            }

                            List<string> itemsToDelete = new List<string>(); //stores all filenames of history.csv entries that need to be removed

                            MethodInvoker LabelUpdate = delegate
                            {
                                list1.Items.Clear();

                                //load all List elements into the ListView for each row
                                foreach (var val in result)
                                {
                                    string camera = val.Split('|')[2];
                                    string success = val.Split('|')[5];
                                    string objects_and_confidence = val.Split('|')[3];
                                    if (!checkListFilters(camera, success, objects_and_confidence)) { continue; } //do not load the entry if a filter applies (checking as early as possible)
                                    string filename = val.Split('|')[0];
                                    string date = val.Split('|')[1];
                                    string object_positions = val.Split('|')[4];

                                    ListViewItem item;
                                    if (success == "true")
                                    {
                                        item = new ListViewItem(new string[] { filename, date, camera, objects_and_confidence, object_positions, "✓" });
                                        item.ForeColor = System.Drawing.Color.Green;
                                    }
                                    else
                                    {
                                        item = new ListViewItem(new string[] { filename, date, camera, objects_and_confidence, object_positions, "X" });
                                    }

                                    list1.Items.Insert(0, item);
                                }

                                ResizeListViews();

                            };
                            Invoke(LabelUpdate);

                            //try to get a better feel how much time this function consumes - Vorlon
                            Log($"...Loaded list in {{yellow}}{SW.ElapsedMilliseconds}ms{{white}}, {list1.Items.Count} lines.");

                        }
                        else
                        {
                            Log($"Error: Could not gain access to history file for {SW.ElapsedMilliseconds}ms - {AppSettings.Settings.HistoryFileName}");

                        }

                    }
                    else
                    {
                        Log("File does not exist yet - cameras/history.csv");
                    }

                }

            }
            catch { }
        }

        //check if a filter applies on given string of history list entry 
        private bool checkListFilters(string cameraname, string success, string objects_and_confidence)
        {
            if (!objects_and_confidence.Contains("person") && cb_filter_person.Checked) { return false; }
            if (!(objects_and_confidence.Contains("car") ||
                  objects_and_confidence.Contains("boat") ||
                  objects_and_confidence.Contains("bicycle") ||
                  objects_and_confidence.Contains("truck") ||
                  objects_and_confidence.Contains("airplane") ||
                  objects_and_confidence.Contains("motorcycle") ||
                  objects_and_confidence.Contains("horse")) && cb_filter_vehicle.Checked) { return false; }
            if (!(objects_and_confidence.Contains("dog") ||
                  objects_and_confidence.Contains("sheep") ||
                  objects_and_confidence.Contains("bird") ||
                  objects_and_confidence.Contains("cow") ||
                  objects_and_confidence.Contains("cat") ||
                  objects_and_confidence.Contains("horse") ||
                  objects_and_confidence.Contains("bear")) && cb_filter_animal.Checked) { return false; }
            if (success != "true" && cb_filter_success.Checked) { return false; } //if filter "only successful detections" is enabled, don't load false alerts
            if (success == "true" && cb_filter_nosuccess.Checked) { return false; } //if filter "only unsuccessful detections" is enabled, don't load true alerts
            if (comboBox_filter_camera.Text != "All Cameras" && cameraname.Trim().ToLower() != comboBox_filter_camera.Text.Trim().ToLower()) { return false; }
            return true;
        }



        //EVENTS

        private void BeginProcessImage(string image_path)
        {

            string filename = Path.GetFileName(image_path);

            MethodInvoker LabelUpdate = delegate { label2.Text = $"Accessing {filename}..."; };
            Invoke(LabelUpdate);

            //output "Processing Image" to Overview Tab
            LabelUpdate = delegate { label2.Text = $"Processing {filename}..."; };
            Invoke(LabelUpdate);

            UpdateQueueLabel();

        }

        private void EndProcessImage(string image_path)
        {

            string filename = Path.GetFileName(image_path);


            //output Running on Overview Tab
            MethodInvoker LabelUpdate = delegate { label2.Text = "Running"; };
            Invoke(LabelUpdate);

            //only update charts if stats tab is open

            LabelUpdate = delegate
            {

                if (tabControl1.SelectedIndex == 1)
                {

                    UpdatePieChart(); UpdateTimeline(); UpdateConfidenceChart();
                }
            };
            Invoke(LabelUpdate);

            //load updated camera stats info in camera tab if a camera is selected
            LabelUpdate = delegate
            {
                if (list2.SelectedItems.Count > 0)
                {
                        //load only stats from Camera.cs object

                        //all camera objects are stored in the list CameraList, so firstly the position (stored in the second column for each entry) is gathered
                        Camera cam = AITOOL.GetCamera(list2.SelectedItems[0].Text);

                        //load cameras stats
                        string stats = $"Alerts: {cam.stats_alerts.ToString()} | Irrelevant Alerts: {cam.stats_irrelevant_alerts.ToString()} | False Alerts: {cam.stats_false_alerts.ToString()}";
                    if (cam.maskManager.masking_enabled)
                    {
                        stats += $" | Mask History Count: {cam.maskManager.last_positions_history.Count()} | Current Dynamic Masks: {cam.maskManager.masked_positions.Count()}";
                    }
                    lbl_camstats.Text = stats;
                }


            };
            Invoke(LabelUpdate);


            UpdateQueueLabel();

        }


        //event: load selected image to picturebox
        private async void list1_SelectedIndexChanged(object sender, EventArgs e) //Bild ändern
        {

            string filename = "";
            try
            {
                if (list1.SelectedItems.Count > 0)
                {

                    //this now stores the full filename
                    filename = list1.SelectedItems[0].Text;

                    if (!String.IsNullOrEmpty(filename) && filename.Contains("\\") && File.Exists(filename))
                    {
                        using (var img = new Bitmap(filename))
                        {
                            pictureBox1.BackgroundImage = new Bitmap(img); //load actual image as background, so that an overlay can be added as the image
                        }
                        showHideMask();
                        lbl_objects.Text = list1.SelectedItems[0].SubItems[3].Text;
                    }
                    else
                    {
                        lbl_objects.Text = "Image not found";
                        pictureBox1.BackgroundImage = null;
                        //delete entry that caused the issue
                        try
                        {
                            DeleteListItem(filename);
                        }
                        //if deleting fails because the filename could not be retrieved, do a complete clean up
                        catch
                        {
                            CleanCSVList();
                            await LoadFromCSVAsync();
                        }
                    }

                }
                else
                {
                    //lbl_objects.Text = "Nothing selected";
                    //pictureBox1.BackgroundImage = null;
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: Loading entry from History list failed. This might have happened because obsolete entries weren't correctly deleted. {Global.ExMsg(ex)} )");

            }



        }

        //event: show mask button clicked
        private void cb_showMask_CheckedChanged(object sender, EventArgs e)
        {
            if (list1.SelectedItems.Count > 0)
            {
                showHideMask();
            }
        }

        //event: show objects button clicked
        private void cb_showObjects_MouseUp(object sender, MouseEventArgs e)
        {
            if (list1.SelectedItems.Count > 0)
            {
                pictureBox1.Refresh();
            }
        }

        //event: show history list filters button clicked
        private void cb_showFilters_CheckedChanged(object sender, EventArgs e)
        {
            if (cb_showFilters.Checked)
            {
                cb_showFilters.Text = "˅ Filter";
                splitContainer1.Panel2Collapsed = false;
            }
            else
            {
                cb_showFilters.Text = "˄ Filter";
                splitContainer1.Panel2Collapsed = true;
            }

            ResizeListViews();

        }

        //event: filter "only revelant alerts" checked or unchecked
        private async void cb_filter_success_CheckedChanged(object sender, EventArgs e)
        {
            await LoadFromCSVAsync();
        }

        //event: filter "only alerts with people" checked or unchecked
        private async void cb_filter_person_CheckedChanged(object sender, EventArgs e)
        {
            await LoadFromCSVAsync();
        }

        //event: filter "only alerts with people" checked or unchecked
        private async void cb_filter_vehicle_CheckedChanged(object sender, EventArgs e)
        {
            await LoadFromCSVAsync();
        }

        //event: filter "only alerts with animals" checked or unchecked
        private async void cb_filter_animal_CheckedChanged(object sender, EventArgs e)
        {
            await LoadFromCSVAsync();
        }

        //event: filter "only false / irrevelant alerts" checked or unchecked
        private async void cb_filter_nosuccess_CheckedChanged(object sender, EventArgs e)
        {
            await LoadFromCSVAsync();
        }

        //event: filter camera dropdown changed
        private async void comboBox_filter_camera_SelectedIndexChanged(object sender, EventArgs e)
        {
            await LoadFromCSVAsync();
        }

        //----------------------------------------------------------------------------------------------------------
        //CAMERAS TAB
        //----------------------------------------------------------------------------------------------------------

        //BASIC METHODS

        // load cameras to camera list
        public void LoadCameras()
        {

            try
            {

                //start by getting last selected camera if any
                string oldname = "";
                if (list2.SelectedItems != null && list2.SelectedItems.Count > 0)
                    oldname = list2.SelectedItems[0].Text;

                list2.Items.Clear();
                comboBox1.Items.Clear();
                comboBox1.Items.Add("All Cameras");
                comboBox_filter_camera.Items.Clear();
                comboBox_filter_camera.Items.Add("All Cameras");

                int i = 0;
                int oldidx = 0;
                foreach (Camera cam in AppSettings.Settings.CameraList)
                {
                    //Add loaded camera to list2
                    ListViewItem item = new ListViewItem(new string[] { cam.name });
                    if (!cam.enabled)
                    {
                        item.ForeColor = System.Drawing.Color.Gray;
                    }
                    //item.Tag = file; //tag is not used anywhere I can see
                    list2.Items.Add(item);
                    //add camera to combobox on overview tab and to camera filter combobox in the History tab 
                    comboBox1.Items.Add($"   {cam.name}");
                    comboBox_filter_camera.Items.Add($"   {cam.name}");
                    if (oldname.Trim().ToLower() == cam.name.Trim().ToLower())
                    {
                        oldidx = i;
                    }
                    i++;

                }

                //select first camera, or last selected camera
                if (list2.Items.Count > 0 && list2.Items.Count >= oldidx)
                {
                    list2.Items[oldidx].Selected = true;
                }


            }
            catch
            {
                Log("ERROR LoadCameras() failed.");
                MessageBox.Show("ERROR LoadCameras() failed.");
            }

        }

        //load existing camera (settings file exists) into CameraList, into Stats dropdown and into History filter dropdown 
        //private string LoadCamera(string config_path)
        //{
        //    //check if camera with specified name or its prefix already exists. If yes, then abort.
        //    foreach (Camera c in AppSettings.Settings.CameraList)
        //    {
        //        if (c.name == Path.GetFileNameWithoutExtension(config_path))
        //        {
        //            return ($"ERROR: Camera name must be unique,{Path.GetFileNameWithoutExtension(config_path)} already exists.");
        //        }
        //        if (c.prefix == System.IO.File.ReadAllLines(config_path)[2].Split('"')[1])
        //        {
        //            return ($"ERROR: Every camera must have a unique prefix ('Input file begins with'), but the prefix of {Path.GetFileNameWithoutExtension(config_path)} equals the prefix of the existing camera {c.name} .");
        //        }
        //    }
        //    Camera cam = new Camera(); //create new camera object
        //    Log("read config");
        //    cam.ReadConfig(config_path); //read camera's config from file
        //    Log("add");
        //    AppSettings.Settings.CameraList.Add(cam); //add created camera object to CameraList

        //    //add camera to combobox on overview tab and to camera filter combobox in the History tab 
        //    comboBox1.Items.Add($"   {cam.name}");
        //    comboBox_filter_camera.Items.Add($"   {cam.name}");

        //    return ($"SUCCESS: {Path.GetFileNameWithoutExtension(config_path)} loaded.");
        //}

        //add camera
        private string AddCamera(Camera cam) //, int history_mins, int mask_create_counter, int mask_remove_counter, double percent_variance)
        {
            //check if camera with specified name already exists. If yes, then abort.
            foreach (Camera c in AppSettings.Settings.CameraList)
            {
                if (c.name.Trim().ToLower() == cam.name.Trim().ToLower())
                {
                    MessageBox.Show($"ERROR: Camera name must be unique,{cam.name} already exists.");
                    return ($"ERROR: Camera name must be unique,{cam.name} already exists.");
                }
            }

            //check if name is empty
            if (cam.name == "")
            {
                MessageBox.Show($"ERROR: Camera name may not be empty.");
                return ($"ERROR: Camera name may not be empty.");
            }


            if (BlueIrisInfo.IsValid && !String.IsNullOrWhiteSpace(BlueIrisInfo.URL))
            {
                //http://10.0.1.99:81/admin?trigger&camera=BACKFOSCAM&user=AITools&pw=haha&memo=[summary]
                cam.trigger_urls_as_string = $"{BlueIrisInfo.URL}/admin?trigger&camera=[camera]&user=ENTERUSERNAMEHERE&pw=ENTERPASSWORDHERE&flagalert=1&memo=[summary]";
            }


            cam.triggering_objects = Global.Split(cam.triggering_objects_as_string, ",").ToArray();   //triggering_objects_as_string.Split(','); //split the row of triggering objects between every ','

            //Split by cr/lf or other common delimiters
            cam.trigger_urls = Global.Split(cam.trigger_urls_as_string, "\r\n|;,").ToArray();  //all trigger urls in an array




            AppSettings.Settings.CameraList.Add(cam); //add created camera object to CameraList

            LoadCameras();

            return ($"SUCCESS: {cam.name} created.");
        }



        //remove camera
        private void RemoveCamera(string name)
        {
            Log($"Removing camera {name}...");
            if (list2.Items.Count > 0) //if list is empty, nothing can be deleted
            {
                if (AppSettings.Settings.CameraList.Exists(x => x.name.ToLower() == name.ToLower())) //check if camera with specified name exists in list
                {

                    //find index of specified camera in list
                    int index = -1;

                    //check for each camera in the cameralist if its name equals the name of the camera that is selected to be deleted
                    for (int i = 0; i < AppSettings.Settings.CameraList.Count; i++)
                    {
                        if (AppSettings.Settings.CameraList[i].name.ToLower().Equals(name.ToLower()))
                        {
                            index = i;

                        }
                    }

                    if (index != -1) //only delete camera if index is known (!= its default value -1)
                    {
                        //AppSettings.Settings.CameraList[index].Delete(); //delete settings file of specified camera

                        //move all cameras following the specified camera one position forward in the list
                        //the position of the specified camera is overridden with the following camera, the position of the following camera is overridden with its follower, and so on
                        for (int i = index; i < AppSettings.Settings.CameraList.Count - 1; i++)
                        {
                            AppSettings.Settings.CameraList[i] = AppSettings.Settings.CameraList[i + 1];
                        }

                        AppSettings.Settings.CameraList.Remove(AppSettings.Settings.CameraList[AppSettings.Settings.CameraList.Count - 1]); //lastly, remove camera from list

                        //remove list2 entry
                        var item = list2.FindItemWithText(name);
                        list2.Items[list2.Items.IndexOf(item)].Remove();

                        //remove camera from combobox on overview tab and from camera filter combobox in the History tab 
                        comboBox1.Items.Remove($"   {name}");
                        comboBox_filter_camera.Items.Remove($"   {name}");

                        //select first camera
                        if (list2.Items.Count > 0)
                        {
                            list2.Items[0].Selected = true;
                        }

                        //if list2 is empty, clear settings fields (to prevent that values of a deleted camera are shown)
                        if (list2.Items.Count == 0)
                        {
                            tbName.Text = "";
                            tbPrefix.Text = "";
                            cb_enabled.Checked = false;
                            CheckBox[] cbarray = new CheckBox[] { cb_airplane, cb_bear, cb_bicycle, cb_bird, cb_boat, cb_bus, cb_car, cb_cat, cb_cow, cb_dog, cb_horse, cb_motorcycle, cb_person, cb_sheep, cb_truck };
                            foreach (CheckBox c in cbarray)
                            {
                                c.Checked = false;
                            }
                            //tbTriggerUrl.Text = "";
                            //cb_telegram.Checked = false;
                            //disable camera settings if there are no cameras setup yet
                            tableLayoutPanel6.Enabled = false;
                        }
                    }
                    else
                    {
                        Log("ERROR: Can't find the selected camera, camera wasn't deleted.");
                    }


                }
            }
        }

        //display camera settings for selected camera
        private void DisplayCameraSettings()
        {
            if (list2.SelectedItems.Count > 0)
            {

                tableLayoutPanel6.Enabled = true;

                tbName.Text = list2.SelectedItems[0].Text; //load name textbox from name in list2

                //load remaining settings from Camera.cs object

                //all camera objects are stored in the list CameraList, so firstly the position (stored in the second column for each entry) is gathered
                Camera cam = AITOOL.GetCamera(tbName.Text);   //int i = AppSettings.Settings.CameraList.FindIndex(x => x.name.Trim().ToLower() == list2.SelectedItems[0].Text.Trim().ToLower());

                //load cameras stats

                string stats = $"Alerts: {cam.stats_alerts.ToString()} | Irrelevant Alerts: {cam.stats_irrelevant_alerts.ToString()} | False Alerts: {cam.stats_false_alerts.ToString()}";

                if (cam.maskManager.masking_enabled)
                {
                    stats += $" | Mask History Count: {cam.maskManager.last_positions_history.Count()} | Current Dynamic Masks: {cam.maskManager.masked_positions.Count()}";
                }
                lbl_camstats.Text = stats;

                //load if ai detection is active for the camera
                if (cam.enabled == true)
                {
                    cb_enabled.Checked = true;
                }
                else
                {
                    cb_enabled.Checked = false;
                }
                tbPrefix.Text = cam.prefix; //load 'input file begins with'
                lbl_prefix.Text = tbPrefix.Text + ".××××××.jpg"; //prefix live preview

                cmbcaminput.Text = cam.input_path;
                cmbcaminput.Items.Clear();
                foreach (string pth in BlueIrisInfo.ClipPaths)
                {
                    cmbcaminput.Items.Add(pth);
                    //try to automatically pick the path that starts with AI if not already set
                    //if ((pth.ToLower().Contains(tbName.Text.ToLower()) || pth.ToLower().Contains(tbPrefix.Text.ToLower())) && string.IsNullOrWhiteSpace(cmbcaminput.Text))
                    //{
                    //    cmbcaminput.Text = pth;
                    //}
                }
                cb_monitorCamInputfolder.Checked = cam.input_path_includesubfolders;

                tb_threshold_lower.Text = cam.threshold_lower.ToString(); //load lower threshold value
                tb_threshold_upper.Text = cam.threshold_upper.ToString(); // load upper threshold value

                //load is masking enabled 
                cb_masking_enabled.Checked = cam.maskManager.masking_enabled;



                //load triggering objects
                //first create arrays with all checkboxes stored in
                CheckBox[] cbarray = new CheckBox[] { cb_airplane, cb_bear, cb_bicycle, cb_bird, cb_boat, cb_bus, cb_car, cb_cat, cb_cow, cb_dog, cb_horse, cb_motorcycle, cb_person, cb_sheep, cb_truck };
                //create array with strings of the triggering_objects related to the checkboxes in the same order
                string[] cbstringarray = new string[] { "airplane", "bear", "bicycle", "bird", "boat", "bus", "car", "cat", "cow", "dog", "horse", "motorcycle", "person", "sheep", "truck" };

                //clear all checkmarks
                foreach (CheckBox cb in cbarray)
                {
                    cb.Checked = false;
                }

                //check for every triggering_object string if it is active in the settings file. If yes, check according checkbox
                for (int j = 0; j < cbarray.Length; j++)
                {
                    if (cam.triggering_objects_as_string.Contains(cbstringarray[j]))
                    {
                        cbarray[j].Checked = true;
                    }
                }
            }
        }



        // SPECIAL METHODS

        //input file begins with live preview
        private void tbPrefix_TextChanged(object sender, EventArgs e)
        {
            lbl_prefix.Text = tbPrefix.Text + ".××××××.jpg";
        }



        //event: camera list another item selected
        private void list2_SelectedIndexChanged(object sender, EventArgs e)
        {
            DisplayCameraSettings(); //display new item's settings
        }

        //event: camera add button
        private void btnCameraAdd_Click(object sender, EventArgs e)
        {

            using (var form = new InputForm("Camera Name:", "New Camera", cbitems: BlueIrisInfo.Cameras))
            {
                var result = form.ShowDialog();
                if (result == DialogResult.OK)
                {
                    Camera cam = new Camera(form.text);

                    string camresult = AddCamera(cam);

                    // Old way...
                    //string name, string prefix, string trigger_urls_as_string, string triggering_objects_as_string, bool telegram_enabled, bool enabled, double cooldown_time, int threshold_lower, int threshold_upper,
                    //                                 string _input_path, bool _input_path_includesubfolders,
                    //                                 bool masking_enabled,
                    //                                 bool trigger_cancels

                    MessageBox.Show(camresult);
                }
            }
        }

        //event: save camera settings button
        private void btnCameraSave_Click_1(object sender, EventArgs e)
        {
            if (list2.Items.Count > 0)
            {
                //check if name is empty
                if (String.IsNullOrWhiteSpace(tbName.Text))
                {
                    DisplayCameraSettings(); //reset displayed settings
                    MessageBox.Show($"WARNING: Camera name may not be empty.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (list2.SelectedItems[0].Text.Trim().ToLower() != tbName.Text.Trim().ToLower())
                {
                    //camera renamed, make sure name doesnt exist
                    Camera CamCheck = AITOOL.GetCamera(tbName.Text, false);
                    if (CamCheck != null)
                    {
                        //Its a dupe
                        MessageBox.Show($"WARNING: Camera name must be unique, but new camera name '{tbName.Text}' already exists.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        DisplayCameraSettings(); //reset displayed settings
                        return;
                    }
                    else
                    {
                        Log($"SUCCESS: Camera {list2.SelectedItems[0].Text} was updated to {tbName.Text}.");
                    }
                }


                Camera CurCam = AITOOL.GetCamera(list2.SelectedItems[0].Text, false);

                if (CurCam == null)
                {
                    //should not happen, but...
                    MessageBox.Show($"WARNING: Camera not found???  '{list2.SelectedItems[0].Text}'", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    DisplayCameraSettings(); //reset displayed settings
                    return;
                }

                //1. GET SETTINGS INPUTTED
                //all checkboxes in one array

                //person,   bicycle,   car,   motorcycle,   airplane,
                //bus,   train,   truck,   boat,   traffic light,   fire hydrant,   stop_sign,
                //parking meter,   bench,   bird,   cat,   dog,   horse,   sheep,   cow,   elephant,
                //bear,   zebra, giraffe,   backpack,   umbrella,   handbag,   tie,   suitcase,
                //frisbee,   skis,   snowboard, sports ball,   kite,   baseball bat,   baseball glove,
                //skateboard,   surfboard,   tennis racket, bottle,   wine glass,   cup,   fork,
                //knife,   spoon,   bowl,   banana,   apple,   sandwich,   orange, broccoli,   carrot,
                //hot dog,   pizza,   donot,   cake,   chair,   couch,   potted plant,   bed, dining table,
                //toilet,   tv,   laptop,   mouse,   remote,   keyboard,   cell phone,   microwave,
                //oven,   toaster,   sink,   refrigerator,   book,   clock,   vase,   scissors,   teddy bear,
                //hair dryer, toothbrush.

                CheckBox[] cbarray = new CheckBox[] { cb_airplane, cb_bear, cb_bicycle, cb_bird, cb_boat, cb_bus, cb_car, cb_cat, cb_cow, cb_dog, cb_horse, cb_motorcycle, cb_person, cb_sheep, cb_truck };
                //create array with strings of the triggering_objects related to the checkboxes in the same order
                string[] cbstringarray = new string[] { "airplane", "bear", "bicycle", "bird", "boat", "bus", "car", "cat", "cow", "dog", "horse", "motorcycle", "person", "sheep", "truck" };

                //go through all checkboxes and write all triggering_objects in one string
                CurCam.triggering_objects_as_string = "";
                for (int i = 0; i < cbarray.Length; i++)
                {
                    if (cbarray[i].Checked == true)
                    {
                        CurCam.triggering_objects_as_string += $"{cbstringarray[i].Trim()}, ";
                    }
                }

                //get lower and upper threshold values from textboxes
                CurCam.threshold_lower = Convert.ToInt32(tb_threshold_lower.Text.Trim());
                CurCam.threshold_upper = Convert.ToInt32(tb_threshold_upper.Text.Trim());

                CurCam.triggering_objects = Global.Split(CurCam.triggering_objects_as_string, ",").ToArray();   //triggering_objects_as_string.Split(','); //split the row of triggering objects between every ','
                CurCam.trigger_urls = Global.Split(CurCam.trigger_urls_as_string, "\r\n|;,").ToArray();  //all trigger urls in an array

                CurCam.name = tbName.Text.Trim();  //just in case we needed to rename it
                CurCam.prefix = tbPrefix.Text.Trim();
                CurCam.enabled = cb_enabled.Checked;
                CurCam.maskManager.masking_enabled = cb_masking_enabled.Checked;
                CurCam.input_path = cmbcaminput.Text.Trim();
                CurCam.input_path_includesubfolders = cb_monitorCamInputfolder.Checked;

                LoadCameras();

                AppSettings.Save();

                UpdateWatchers();

                Log("Camera saved.");

                MessageBox.Show("Camera saved", "", MessageBoxButtons.OK, MessageBoxIcon.Information);


                ////2. UPDATE SETTINGS
                //// save new camera settings, display result in MessageBox
                //string result = UpdateCamera(list2.SelectedItems[0].Text, tbName.Text, tbPrefix.Text, tbTriggerUrl.Text, triggering_objects_as_string, cb_telegram.Checked, cb_enabled.Checked, cooldown_time, threshold_lower, threshold_upper,
                //                             cmbcaminput.Text, cb_monitorCamInputfolder.Checked,
                //                             cb_masking_enabled.Checked,
                //                             cb_TriggerCancels.Checked); //, history_mins, mask_create_counter, mask_remove_counter, percent_variance);



                //1     2       3                             4                       5                 6        7              8                9
                //name, prefix, triggering_objects_as_string, trigger_urls_as_string, telegram_enabled, enabled, cooldown_time, threshold_lower, threshold_upper,
                //                                                               10           11  
                //                                                               _input_path, _input_path_includesubfolders,
                //                                                          12   masking_enabled,
                //                                                          13   trigger_cancel



            }
            DisplayCameraSettings();
        }

        //event: delete camera button
        private void btnCameraDel_Click(object sender, EventArgs e)
        {
            if (list2.Items.Count > 0)
            {
                using (var form = new InputForm($"Delete camera {list2.SelectedItems[0].Text} ?", "Delete Camera?", false))
                {
                    var result = form.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        //Log("about to del cam");
                        RemoveCamera(list2.SelectedItems[0].Text);
                    }
                }
            }
        }

        //event: DELETE key pressed
        private void list2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (list2.Items.Count > 0)
                {
                    using (var form = new InputForm($"Delete camera {list2.SelectedItems[0].Text} ?", "Delete Camera?", false))
                    {
                        var result = form.ShowDialog();
                        if (result == DialogResult.OK)
                        {
                            RemoveCamera(list2.SelectedItems[0].Text);
                        }
                    }
                }
            }
        }

        //event: leaving empty lower confidence limit textbox
        private void tb_threshold_lower_Leave(object sender, EventArgs e)
        {
            if (tb_threshold_lower.Text == "")
            {
                tb_threshold_lower.Text = "0";
            }
        }

        //event: leaving empty upper confidence limit textbox
        private void tb_threshold_upper_Leave(object sender, EventArgs e)
        {
            if (tb_threshold_upper.Text == "")
            {
                tb_threshold_upper.Text = "100";
            }
        }



        //----------------------------------------------------------------------------------------------------------
        //SETTING TAB
        //----------------------------------------------------------------------------------------------------------


        //settings save button
        private void BtnSettingsSave_Click_1(object sender, EventArgs e)
        {
            Log($"Saving settings to {AppSettings.Settings.SettingsFileName}");
            //save inputted settings into App.settings
            AppSettings.Settings.input_path = cmbInput.Text;
            AppSettings.Settings.input_path_includesubfolders = cb_inputpathsubfolders.Checked;
            AppSettings.Settings.deepstack_url = tbDeepstackUrl.Text;
            AppSettings.Settings.telegram_chatids = Global.Split(tb_telegram_chatid.Text, "|;,", true, true);
            AppSettings.Settings.telegram_token = tb_telegram_token.Text;
            AppSettings.Settings.log_everything = cb_log.Checked;
            AppSettings.Settings.send_errors = cb_send_errors.Checked;
            AppSettings.Settings.startwithwindows = cbStartWithWindows.Checked;

            Global.Startup(AppSettings.Settings.startwithwindows);

            if (AppSettings.Save())
            {
                Log("...Saved.");
            }
            else
            {
                Log("...Not saved.  No changes?");
            }
            //update variables
            //input_path = AppSettings.Settings.input_path;
            //deepstack_url = AppSettings.Settings.deepstack_url;
            //telegram_chatid = AppSettings.Settings.telegram_chatid;
            //telegram_chatids = telegram_chatid.Replace(" ", "").Split(','); //for multiple Telegram chats that receive alert images
            //telegram_token = AppSettings.Settings.telegram_token;
            //log_everything = AppSettings.Settings.log_everything;
            //send_errors = AppSettings.Settings.send_errors;

            //update fswatcher to watch new input folder
            UpdateWatchers();

            //clean history.csv database
            CleanCSVList();

            //LoadList();
        }

        //input path select dialog button
        private void btn_input_path_Click(object sender, EventArgs e)
        {
            using (CommonOpenFileDialog dialog = new CommonOpenFileDialog())
            {
                if (!string.IsNullOrEmpty(cmbInput.Text))
                {
                    dialog.InitialDirectory = cmbInput.Text;

                }
                dialog.IsFolderPicker = true;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    cmbInput.Text = dialog.FileName;
                }
            }
        }

        //open log button
        private void btn_open_log_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists(AppSettings.Settings.LogFileName))
            {
                System.Diagnostics.Process.Start(AppSettings.Settings.LogFileName);
                lbl_errors.Text = "";
            }
            else
            {
                MessageBox.Show("log missing");
            }

        }

        //ask before closing AI Tool to prevent accidentally closing
        private void Shell_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (AppSettings.Settings.close_instantly <= 0) //if it's eigther enabled or not set  -1 = not set | 0 = ask for confirmation | 1 = don't ask
            {
                using (var form = new InputForm($"Stop and close AI Tool?", "AI Tool", false))
                {
                    var result = form.ShowDialog();
                    if (AppSettings.Settings.close_instantly == -1)
                    {
                        //if it's the first time, ask if the confirmation dialog should ever appear again
                        using (var form1 = new InputForm($"Confirm closing AI Tool every time?", "AI Tool", false, "NO, Never!", "YES"))
                        {
                            var result1 = form1.ShowDialog();
                            if (result1 == DialogResult.Cancel)
                            {
                                AppSettings.Settings.close_instantly = 0;
                            }
                            else
                            {
                                AppSettings.Settings.close_instantly = 1;
                            }
                        }
                    }

                    e.Cancel = (result == DialogResult.Cancel);
                }
            }

            Global_GUI.SaveWindowState(this);

            AppSettings.Save();  //save settings in any case


        }

        private void Shell_Load(object sender, EventArgs e)
        {
            Global_GUI.RestoreWindowState(this);
        }
        private void SaveDeepStackTab()
        {

            if (DeepStackServerControl == null)
                DeepStackServerControl = new DeepStack(AppSettings.Settings.deepstack_adminkey, AppSettings.Settings.deepstack_apikey, AppSettings.Settings.deepstack_mode, AppSettings.Settings.deepstack_sceneapienabled, AppSettings.Settings.deepstack_faceapienabled, AppSettings.Settings.deepstack_detectionapienabled, AppSettings.Settings.deepstack_port);

            DeepStackServerControl.GetDeepStackRun();

            if (RB_Medium.Checked)
                AppSettings.Settings.deepstack_mode = "Medium";
            if (RB_Low.Checked)
                AppSettings.Settings.deepstack_mode = "Low";
            if (RB_High.Checked)
                AppSettings.Settings.deepstack_mode = "High";

            AppSettings.Settings.deepstack_detectionapienabled = Chk_DetectionAPI.Checked;
            AppSettings.Settings.deepstack_faceapienabled = Chk_FaceAPI.Checked;
            AppSettings.Settings.deepstack_sceneapienabled = Chk_SceneAPI.Checked;
            AppSettings.Settings.deepstack_autostart = Chk_AutoStart.Checked;
            AppSettings.Settings.deepstack_debug = Chk_DSDebug.Checked;
            AppSettings.Settings.deepstack_highpriority = chk_HighPriority.Checked;
            AppSettings.Settings.deepstack_adminkey = Txt_AdminKey.Text.Trim();
            AppSettings.Settings.deepstack_apikey = Txt_APIKey.Text.Trim();
            AppSettings.Settings.deepstack_installfolder = Txt_DeepStackInstallFolder.Text.Trim();
            AppSettings.Settings.deepstack_port = Txt_Port.Text.Trim();


            AppSettings.Save();


            if (DeepStackServerControl.IsInstalled)
            {
                if (DeepStackServerControl.IsStarted)
                {


                    if (DeepStackServerControl.IsActivated)
                    {
                        MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*RUNNING*"; };
                        Invoke(LabelUpdate);

                        Btn_Start.Enabled = false;
                        Btn_Stop.Enabled = true;
                    }
                    else
                    {
                        MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*NOT ACTIVATED, RUNNING*"; };
                        Invoke(LabelUpdate);

                        Btn_Start.Enabled = false;
                        Btn_Stop.Enabled = true;
                    }
                }
                else
                {
                    MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*NOT RUNNING*"; };
                    Invoke(LabelUpdate);

                    Btn_Start.Enabled = true;
                    Btn_Stop.Enabled = false;
                }
            }
            else
            {
                Btn_Start.Enabled = false;
                Btn_Stop.Enabled = false;
                MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*NOT INSTALLED*"; };
                Invoke(LabelUpdate);


            }

            DeepStackServerControl.Update(AppSettings.Settings.deepstack_adminkey, AppSettings.Settings.deepstack_apikey, AppSettings.Settings.deepstack_mode, AppSettings.Settings.deepstack_sceneapienabled, AppSettings.Settings.deepstack_faceapienabled, AppSettings.Settings.deepstack_detectionapienabled, AppSettings.Settings.deepstack_port);

        }

        private async void LoadDeepStackTab(bool StartIfNeeded)
        {

            try
            {
                if (DeepStackServerControl == null)
                    DeepStackServerControl = new DeepStack(AppSettings.Settings.deepstack_adminkey, AppSettings.Settings.deepstack_apikey, AppSettings.Settings.deepstack_mode, AppSettings.Settings.deepstack_sceneapienabled, AppSettings.Settings.deepstack_faceapienabled, AppSettings.Settings.deepstack_detectionapienabled, AppSettings.Settings.deepstack_port);


                //first update the port in the deepstack_url if found
                //string prt = Global.GetWordBetween(AppSettings.Settings.deepstack_url, ":", " |/");
                //if (!string.IsNullOrEmpty(prt) && (Convert.ToInt32(prt) > 0))
                //{
                //    DeepStackServerControl.Port = prt;
                //}

                //This will OVERRIDE the port if the deepstack processes found running already have a different port, mode, etc:
                DeepStackServerControl.GetDeepStackRun();

                if (DeepStackServerControl.Mode.ToLower() == "medium")
                    RB_Medium.Checked = true;
                if (DeepStackServerControl.Mode.ToLower() == "low")
                    RB_Low.Checked = true;
                if (DeepStackServerControl.Mode.ToLower() == "high")
                    RB_High.Checked = true;

                Chk_DetectionAPI.Checked = DeepStackServerControl.DetectionAPIEnabled;
                Chk_FaceAPI.Checked = DeepStackServerControl.FaceAPIEnabled;
                Chk_SceneAPI.Checked = DeepStackServerControl.SceneAPIEnabled;

                //have seen a few cases nothing is checked but it is required
                if (!Chk_DetectionAPI.Checked && !Chk_FaceAPI.Checked && !Chk_SceneAPI.Checked)
                {
                    Chk_DetectionAPI.Checked = true;
                    DeepStackServerControl.DetectionAPIEnabled = true;
                }

                Chk_AutoStart.Checked = AppSettings.Settings.deepstack_autostart;
                Chk_DSDebug.Checked = AppSettings.Settings.deepstack_debug;
                chk_HighPriority.Checked = AppSettings.Settings.deepstack_highpriority;
                Txt_AdminKey.Text = DeepStackServerControl.AdminKey;
                Txt_APIKey.Text = DeepStackServerControl.APIKey;
                Txt_DeepStackInstallFolder.Text = DeepStackServerControl.DeepStackFolder;
                Txt_Port.Text = DeepStackServerControl.Port;

                //if (prt != Txt_Port.Text)
                //{
                //    //server:port/maybe/more/path
                //    string serv = Global.GetWordBetween(AppSettings.Settings.deepstack_url, "", ":");
                //    if (!string.IsNullOrEmpty(serv))
                //    {
                //        tbDeepstackUrl.Text = serv + ":" + Txt_Port.Text;
                //        //AppSettings.Settings.deepstack_url = serv + ":" + Txt_Port.Text;
                //        //AppSettings.Settings.deepstack_url = tbDeepstackUrl.Text;
                //        //AppSettings.Save();
                //    }
                //}

                if (DeepStackServerControl.IsInstalled)
                {
                    if (DeepStackServerControl.IsStarted && !DeepStackServerControl.HasError)
                    {
                        if (DeepStackServerControl.IsActivated && (DeepStackServerControl.VisionDetectionRunning || DeepStackServerControl.DetectionAPIEnabled))
                        {

                            MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*RUNNING*"; };
                            Invoke(LabelUpdate);

                            Btn_Start.Enabled = false;
                            Btn_Stop.Enabled = true;
                        }
                        else if (!DeepStackServerControl.IsActivated)
                        {
                            MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*NOT ACTIVATED, RUNNING*"; };
                            Invoke(LabelUpdate);

                            Btn_Start.Enabled = false;
                            Btn_Stop.Enabled = true;
                        }
                        else if (!DeepStackServerControl.VisionDetectionRunning || DeepStackServerControl.DetectionAPIEnabled)
                        {
                            MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*DETECTION API NOT RUNNING*"; };
                            Invoke(LabelUpdate);

                            Btn_Start.Enabled = false;
                            Btn_Stop.Enabled = true;
                        }

                    }
                    else if (DeepStackServerControl.HasError)
                    {
                        MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*ERROR*"; };
                        Invoke(LabelUpdate);

                        Btn_Start.Enabled = false;
                        Btn_Stop.Enabled = true;
                    }
                    else
                    {
                        MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*NOT RUNNING*"; };
                        Invoke(LabelUpdate);

                        Btn_Start.Enabled = true;
                        Btn_Stop.Enabled = false;
                        if (Chk_AutoStart.Checked && StartIfNeeded)
                        {
                            if (await DeepStackServerControl.StartAsync())
                            {
                                if (DeepStackServerControl.IsStarted && !DeepStackServerControl.HasError)
                                {
                                    LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*RUNNING*"; };
                                    Invoke(LabelUpdate);
                                    Btn_Start.Enabled = false;
                                    Btn_Stop.Enabled = true;
                                }
                                else if (DeepStackServerControl.HasError)
                                {
                                    LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*ERROR*"; };
                                    Invoke(LabelUpdate);

                                    Btn_Start.Enabled = false;
                                    Btn_Stop.Enabled = true;
                                }

                            }
                            else
                            {
                                LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*ERROR*"; };
                                Invoke(LabelUpdate);

                                Btn_Start.Enabled = false;
                                Btn_Stop.Enabled = true;
                            }
                        }
                    }
                }
                else
                {
                    Btn_Start.Enabled = false;
                    Btn_Stop.Enabled = false;
                    MethodInvoker LabelUpdate = delegate { Lbl_BlueStackRunning.Text = "*NOT INSTALLED*"; };
                    Invoke(LabelUpdate);


                }

            }
            catch (Exception ex)
            {

                Log(Global.ExMsg(ex));
            }
        }

        private async void Btn_Start_Click(object sender, EventArgs e)
        {
            Lbl_BlueStackRunning.Text = "STARTING...";
            Btn_Start.Enabled = false;
            Btn_Stop.Enabled = false;
            SaveDeepStackTab();
            await DeepStackServerControl.StartAsync();
            LoadDeepStackTab(true);
        }

        private void Btn_Save_Click(object sender, EventArgs e)
        {
            SaveDeepStackTab();
        }

        private async void Btn_Stop_Click(object sender, EventArgs e)
        {
            Lbl_BlueStackRunning.Text = "STOPPING...";
            Btn_Start.Enabled = false;
            Btn_Stop.Enabled = false;
            await DeepStackServerControl.StopAsync();
            LoadDeepStackTab(false);
        }

        private void btnStopscroll_Click(object sender, EventArgs e)
        {
            RTFLogger.AutoScroll = false;
        }

        private void btnViewLog_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists(AppSettings.Settings.LogFileName))
            {
                System.Diagnostics.Process.Start(AppSettings.Settings.LogFileName);
                lbl_errors.Text = "";
            }
            else
            {
                MessageBox.Show("log missing");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists(AppSettings.Settings.LogFileName))
            {
                System.Diagnostics.Process.Start(AppSettings.Settings.LogFileName);
                lbl_errors.Text = "";
            }
            else
            {
                MessageBox.Show("log missing");
            }
        }

        private void Chk_AutoScroll_CheckedChanged(object sender, EventArgs e)
        {
            if (Chk_AutoScroll.Checked)
            {
                RTFLogger.AutoScroll = true;
            }
            else
            {
                RTFLogger.AutoScroll = false;
            }
        }

        private void tableLayoutPanel7_Paint(object sender, PaintEventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (CommonOpenFileDialog dialog = new CommonOpenFileDialog())
            {
                if (!string.IsNullOrEmpty(cmbcaminput.Text))
                {
                    dialog.InitialDirectory = cmbcaminput.Text;

                }
                dialog.IsFolderPicker = true;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    cmbcaminput.Text = dialog.FileName;
                }
            }
        }


        private void BtnDynamicMaskingSettings_Click(object sender, EventArgs e)
        {
            using (Frm_DynamicMasking frm = new Frm_DynamicMasking())
            {

                Camera cam = AITOOL.GetCamera(list2.SelectedItems[0].Text);

                //Merge ClassObject's code
                frm.num_history_mins.Value = cam.maskManager.history_save_mins;//load minutes to retain history objects that have yet to become masks
                frm.num_mask_create.Value = cam.maskManager.history_threshold_count; // load mask create counter
                frm.num_mask_remove.Value = cam.maskManager.mask_counter_default; //load mask remove counter
                frm.num_percent_var.Value = (decimal)cam.maskManager.thresholdPercent * 100;



                if (frm.ShowDialog() == DialogResult.OK)
                {
                    ////get masking values from textboxes


                    Int32.TryParse(frm.num_history_mins.Text, out int history_mins);
                    Int32.TryParse(frm.num_mask_create.Text, out int mask_create_counter);
                    Int32.TryParse(frm.num_mask_remove.Text, out int mask_remove_counter);
                    Int32.TryParse(frm.num_percent_var.Text, out int variance);

                    ////convert to percent
                    Double percent_variance = (double)variance / 100;

                    cam.maskManager.history_save_mins = history_mins;
                    cam.maskManager.history_threshold_count = mask_create_counter;
                    cam.maskManager.mask_counter_default = mask_remove_counter;
                    cam.maskManager.thresholdPercent = percent_variance;

                    cam.maskManager.masking_enabled = cb_masking_enabled.Checked;

                    AppSettings.Save();

                }
            }
        }

        private void btnDetails_Click(object sender, EventArgs e)
        {

            using (Frm_DynamicMaskDetails frm = new Frm_DynamicMaskDetails())
            {

                //all camera objects are stored in the list CameraList, so firstly the position (stored in the second column for each entry) is gathered
                int i = AppSettings.Settings.CameraList.FindIndex(x => x.name.Trim().ToLower() == list2.SelectedItems[0].Text.Trim().ToLower());

                frm.cam = AppSettings.Settings.CameraList[i];

                frm.ShowDialog();
            }


        }

        private void QueueLblTmr_Tick(object sender, EventArgs e)
        {

            UpdateQueueLabel();
        }

        private void UpdateQueueLabel()
        {
            MethodInvoker LabelUpdate = delegate { lblQueue.Text = $"Images in queue: {ImageProcessQueue.Count}, Max: {qsizecalc.Max} ({qcalc.Max}ms), Average: {qsizecalc.Average.ToString("#####")} ({qcalc.Average.ToString("#####")}ms queue wait time)"; };
            Invoke(LabelUpdate);


        }

        private void btnCustomMask_Click(object sender, EventArgs e)
        {
            using (Frm_CustomMasking frm = new Frm_CustomMasking())
            {
                Camera cam = AITOOL.GetCamera(list2.SelectedItems[0].Text);
                frm.cam = cam;

                if (frm.ShowDialog() == DialogResult.OK)
                {
                    cam.mask_brush_size = frm.brushSize;
                }
            }

        }


        private void btnActions_Click(object sender, EventArgs e)
        {
            using (Frm_LegacyActions frm = new Frm_LegacyActions())
            {


                Camera cam = AITOOL.GetCamera(list2.SelectedItems[0].Text);
                frm.cam = cam;

                string tfixed = string.Join("\r\n", Global.Split(cam.trigger_urls_as_string, "\r\n|;,"));
                frm.tbTriggerUrl.Text = tfixed;
                frm.tb_cooldown.Text = cam.cooldown_time.ToString(); //load cooldown time
                //load telegram image sending on/off option
                frm.cb_telegram.Checked = cam.telegram_enabled;

                frm.cb_TriggerCancels.Checked = cam.trigger_url_cancels;
                frm.cb_copyAlertImages.Checked = cam.Action_image_copy_enabled;
                frm.cb_UseOriginalFilename.Checked = cam.Action_image_copy_original_name;
                frm.tb_network_folder.Text = cam.Action_network_folder;
                frm.cb_RunProgram.Checked = cam.Action_RunProgram;

                if (frm.ShowDialog() == DialogResult.OK)
                {
                    cam.trigger_urls_as_string = string.Join(",", Global.Split(frm.tbTriggerUrl.Text.Trim(), "\r\n|;,"));
                    cam.cooldown_time = Convert.ToDouble(frm.tb_cooldown.Text.Trim());
                    cam.telegram_enabled = frm.cb_telegram.Checked;
                    cam.trigger_url_cancels = frm.cb_TriggerCancels.Checked;
                    cam.Action_image_copy_enabled = frm.cb_copyAlertImages.Checked;
                    cam.Action_network_folder = frm.tb_network_folder.Text.Trim();
                    cam.Action_image_copy_original_name = frm.cb_UseOriginalFilename.Checked;
                    cam.Action_RunProgram = frm.cb_RunProgram.Checked;
                    cam.Action_RunProgramString = frm.tb_RunExternalProgram.Text;

                    AppSettings.Save();

                }
            }
        }

        private void btnActions_Click_1(object sender, EventArgs e)
        {
            using (Frm_LegacyActions frm = new Frm_LegacyActions())
            {


                Camera cam = AITOOL.GetCamera(list2.SelectedItems[0].Text);
                
                frm.cam = cam;

                string tfixed = string.Join("\r\n", Global.Split(cam.trigger_urls_as_string, "\r\n|;,"));
                frm.tbTriggerUrl.Text = tfixed;
                frm.tb_cooldown.Text = cam.cooldown_time.ToString(); //load cooldown time
                //load telegram image sending on/off option
                frm.cb_telegram.Checked = cam.telegram_enabled;

                frm.cb_TriggerCancels.Checked = cam.trigger_url_cancels;

                frm.cb_copyAlertImages.Checked = cam.Action_image_copy_enabled;
                frm.cb_UseOriginalFilename.Checked = cam.Action_image_copy_original_name;
                frm.tb_network_folder.Text = cam.Action_network_folder;

                frm.cb_RunProgram.Checked = cam.Action_RunProgram;
                frm.tb_RunExternalProgram.Text = cam.Action_RunProgramString;
                frm.tb_RunExternalProgramArgs.Text = cam.Action_RunProgramArgsString;

                frm.cb_PlaySound.Checked = cam.Action_PlaySounds;
                frm.tb_Sounds.Text = cam.Action_Sounds;

                frm.cb_MQTT_enabled.Checked = cam.Action_mqtt_enabled;
                frm.tb_MQTT_Payload.Text = cam.Action_mqtt_payload;
                frm.tb_MQTT_Topic.Text = cam.Action_mqtt_topic;


                if (frm.ShowDialog() == DialogResult.OK)
                {
                    cam.trigger_urls_as_string = string.Join(",", Global.Split(frm.tbTriggerUrl.Text.Trim(), "\r\n|;,"));
                    cam.cooldown_time = Convert.ToDouble(frm.tb_cooldown.Text.Trim());
                    cam.telegram_enabled = frm.cb_telegram.Checked;
                    cam.trigger_url_cancels = frm.cb_TriggerCancels.Checked;

                    cam.Action_image_copy_enabled = frm.cb_copyAlertImages.Checked;
                    cam.Action_network_folder = frm.tb_network_folder.Text.Trim();
                    cam.Action_image_copy_original_name = frm.cb_UseOriginalFilename.Checked;

                    cam.Action_RunProgram = frm.cb_RunProgram.Checked;
                    cam.Action_RunProgramString = frm.tb_RunExternalProgram.Text.Trim();
                    cam.Action_RunProgramArgsString = frm.tb_RunExternalProgramArgs.Text.Trim();

                    cam.Action_PlaySounds = frm.cb_PlaySound.Checked;
                    cam.Action_Sounds = frm.tb_Sounds.Text.Trim();

                    cam.Action_mqtt_enabled = frm.cb_MQTT_enabled.Checked;
                    cam.Action_mqtt_payload = frm.tb_MQTT_Payload.Text.Trim();
                    cam.Action_mqtt_topic = frm.tb_MQTT_Topic.Text.Trim();

                    AppSettings.Save();

                }
            }
        }
    }


    //classes for AI analysis

    class Response
    {

        public bool success { get; set; }
        public Object[] predictions { get; set; }

    }

    class Object
    {

        public string label { get; set; }
        public float confidence { get; set; }
        public int y_min { get; set; }
        public int x_min { get; set; }
        public int y_max { get; set; }
        public int x_max { get; set; }

    }


    //enhanced TableLayoutPanel loads faster
    public partial class DBLayoutPanel:TableLayoutPanel
    {
        public DBLayoutPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
              ControlStyles.OptimizedDoubleBuffer |
              ControlStyles.UserPaint, true);
        }

        public DBLayoutPanel(IContainer container)
        {
            container.Add(this);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
              ControlStyles.OptimizedDoubleBuffer |
              ControlStyles.UserPaint, true);
        }
    }
}


