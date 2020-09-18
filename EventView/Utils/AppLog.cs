using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Diagnostics.Symbols;

namespace PerfEventView.Utils
{
    public static class AppLog
    {
        /// <summary>
        /// Returns true if you have access to the file share where we log feedback
        /// </summary>
        public static bool CanSendFeedback
        {
            get
            {
#if PUBLIC_BUILD
                return false;
#else
                if (!s_CanSendFeedback.HasValue)
                {
                    if (s_IsUnderTest)
                    {
                        s_CanSendFeedback = false;          // Don't send feedback about test runs.  
                    }
                    else
                    {
                        // Have we tried to probe for the existance of \\clrmain?
                        if (s_ProbedForFeedbackAt.Ticks == 0)
                        {
                            s_ProbedForFeedbackAt = DateTime.Now;
                            // Only collect data in the REDMOND domain EUROPE has rules about telemetry.  
                            var userDomain = Environment.GetEnvironmentVariable("USERDOMAIN");
                            if (userDomain != "REDMOND")
                            {
                                s_CanSendFeedback = false;
                            }
                            else
                            {
                                s_CanSendFeedback = SymbolPath.ComputerNameExists(FeedbackServer) && WriteFeedbackToLog(FeedbackFilePath, "");
                            }
                        }
                        else
                        {
                            // Yes, see what has become of it.   
                            int msecSinceProbe = (int)(DateTime.Now - s_ProbedForFeedbackAt).TotalMilliseconds;
                            if (msecSinceProbe < 800)
                            {
                                Thread.Sleep(msecSinceProbe);       // We probed not to long ago, give it some time
                            }

                            if (!s_CanSendFeedback.HasValue)
                            {
                                s_CanSendFeedback = false;          //Give up if we don't have an answer by now. 
                            }
                        }
                    }
                }
                return s_CanSendFeedback.Value;
#endif
            }
        }
        /// <summary>
        /// Are we internal to Microsoft (and thus can have experimental features. 
        /// </summary>
        public static bool InternalUser
        {
            get
            {
                if (!s_InternalUser.HasValue)
                {
                    s_InternalUser = s_IsUnderTest || SymbolPath.ComputerNameExists(FeedbackServer, 400);
                }

                return s_InternalUser.Value;
            }
        }
        /// <summary>
        /// Log that the event 'eventName' with an optional string arg happened.  Will
        /// get stamped with the time, user, and session ID.  
        /// </summary>
        public static void LogUsage(string eventName, string arg1 = "", string arg2 = "")
        {
#if !PUBLIC_BUILD
            if (!CanSendFeedback)
            {
                return;
            }

            try
            {
                var usagePath = UsageFilePath;
                var userName = Environment.GetEnvironmentVariable("USERNAME");

                using (var writer = File.AppendText(usagePath))
                {
                    var now = DateTime.Now;
                    if (s_startTime.Ticks == 0)
                    {
                        s_startTime = now;
                    }

                    var secFromStart = (now - s_startTime).TotalSeconds;

                    var sessionID = (uint)(s_startTime.Ticks / 100000);
                    // SessionID, user, secondFromStart messageKind, arg 
                    writer.WriteLine("{0},{1},{2:f1},{3},\"{4}\",\"{5}\"", sessionID, userName, secFromStart, eventName, arg1, arg2);
                }

                // Keep the file to 10 meg;
                // Note that the move might fail, but that is OK.  
                if (new FileInfo(usagePath).Length > 10000000)
                {
                    File.Move(usagePath, Path.ChangeExtension(usagePath, ".prev.csv"));
                }
            }
            catch (Exception) { }
#endif
        }
        /// <summary>
        /// Called if you wish to send feedback to the developer.  Returns true if successful
        /// We segregate feedback into crashes and suggestions.  
        /// </summary>
        public static bool SendFeedback(string message, bool crash)
        {
#if PUBLIC_BUILD
            return false;
#else
            if (!CanSendFeedback)
            {
                return false;
            }

            StringWriter sw = new StringWriter();
            var userName = Environment.GetEnvironmentVariable("USERNAME");
            var userDomain = Environment.GetEnvironmentVariable("USERDOMAIN");

            var issueID = userName.Replace(" ", "") + "-" + DateTime.Now.ToString("yyyy'-'MM'-'dd'.'HH'.'mm'.'ss");
            string feedbackFile = FeedbackFilePath;
            var logPath = Path.Combine(FeedbackDirectory, "UserLog." + issueID + ".txt");

            sw.WriteLine("**********************************************************************");
            sw.WriteLine("OpenIssueID: {0}", issueID);
            sw.WriteLine("Date: {0}", DateTime.Now);
            sw.WriteLine("UserName: {0}", userName);
            sw.WriteLine("UserDomain: {0}", userDomain);
            sw.WriteLine("PerfView Version Number: {0}", VersionNumber);
            sw.WriteLine("PerfView Build Date: {0}", BuildDate);

            try
            {
                // Capture the user log, to see how we got here.  if it is less than 20 Meg.  
                //if (File.Exists(App.LogFileName) && (new FileInfo(App.LogFileName)).Length < 20000000)
                //{
                //    File.Copy(App.LogFileName, logPath, true);
                //}

                sw.WriteLine("UserLog: {0}", logPath);
            }
            catch { };

            sw.WriteLine("Message:");
            sw.Write("    ");
            sw.WriteLine(message.Replace("\n", "\n    "));
            return WriteFeedbackToLog(feedbackFile, sw.ToString());
#endif
        }
        public static string VersionNumber
        {
            get
            {
                // Update the AssemblyFileVersion attribute in AssemblyInfo.cs to update the version number 
                var fileVersion = (AssemblyFileVersionAttribute)(Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0]);
                return fileVersion.Version;
            }
        }
        public static string BuildDate
        {
            get
            {
                //var buildDateAttribute = typeof(AppLog).Assembly.GetCustomAttributes<BuildDateAttribute>().FirstOrDefault();
                //return buildDateAttribute?.BuildDate ?? "Unknown";
                return "Unknown";
            }
        }

        #region private


        private static string FeedbackServer { get { return "clrMain"; } }
        private static string UsageFilePath { get { return Path.Combine(FeedbackDirectory, "PerfViewUsage.csv"); } }
        internal static string FeedbackFilePath { get { return Path.Combine(FeedbackDirectory, "PerfViewFeedback.txt"); } }
        private static string CrashLogFilePath { get { return Path.Combine(FeedbackDirectory, "PerfViewCrashes.txt"); } }
        private static string FeedbackDirectory
        {
            get
            {
                return @"\\" + FeedbackServer + @"\public\writable\perfView";
            }
        }

        private static DateTime s_startTime;    // used as a unique ID for the launch of the program (for SQM style logging)    
        internal static bool s_IsUnderTest; // set from tests: indicates we're in a test
        private static bool? s_InternalUser;
#if !PUBLIC_BUILD
        private static DateTime s_ProbedForFeedbackAt;
        private static bool? s_CanSendFeedback;
#endif
        private static bool WriteFeedbackToLog(string filePath, string message)
        {
            // Try 5 times (50 msec) to write the file.
            DateTime start = DateTime.UtcNow;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using (var writer = new StreamWriter(filePath, true))   // open for appending. 
                    {
                        writer.Write(message);
                    }

                    return true;
                }
                catch (Exception) { }

                if ((DateTime.UtcNow - start).TotalMilliseconds > 50)
                {
                    break;
                }

                System.Threading.Thread.Sleep(10);
            }
            return false;
        }
        #endregion
    }
}