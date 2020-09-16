using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Utilities;

namespace PerfView.PerfViewData
{
    public class PerfViewAspNetStats : PerfViewHtmlReport
    {
        public PerfViewAspNetStats(PerfViewFile dataFile) : base(dataFile, "Asp.Net Stats") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            var dispatcher = dataFile.Events.GetSource();
            var aspNet = new AspNetTraceEventParser(dispatcher);

            m_requests = new List<AspNetRequest>();
            var requestByID = new Dictionary<Guid, AspNetRequest>();

            var startIntervalMSec = 0;
            var totalIntervalMSec = dataFile.SessionDuration.TotalMilliseconds;

            var bucketIntervalMSec = 1000;
            var numBuckets = Math.Max(1, (int)(totalIntervalMSec / bucketIntervalMSec));

            var GCType = "Unknown";
            var requestsRecieved = 0;

            var byTimeStats = new ByTimeRequestStats[numBuckets];
            for (int i = 0; i < byTimeStats.Length; i++)
            {
                byTimeStats[i] = new ByTimeRequestStats();
            }

            var requestsProcessing = 0;

            dispatcher.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
            {
                if (data.ProcessID == 0)    // Non-idle time.  
                {
                    return;
                }

                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].CpuMSec++;
                }
            };

            dispatcher.Clr.RuntimeStart += delegate (RuntimeInformationTraceData data)
            {
                if ((data.StartupFlags & StartupFlags.SERVER_GC) != 0)
                {
                    GCType = "Server";
                }
                else
                {
                    GCType = "Client";
                }
            };

            dispatcher.Clr.ContentionStart += delegate (ContentionStartTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].Contentions++;
                }
            };

            dispatcher.Clr.GCStop += delegate (GCEndTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].NumGcs++;
                    if (data.Depth >= 2)
                    {
                        byTimeStats[idx].NumGen2Gcs++;
                    }
                }
            };

            bool seenBadAllocTick = false;
            dispatcher.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    var valueMB = data.GetAllocAmount(ref seenBadAllocTick) / 1000000.0f;

                    byTimeStats[idx].GCHeapAllocMB += valueMB;
                }
            };

            dispatcher.Clr.GCHeapStats += delegate (GCHeapStatsTraceData data)
            {
                // TODO should it be summed over processes? 
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    var totalSize = data.GenerationSize0 + data.GenerationSize1 + data.GenerationSize2 + data.GenerationSize3;
                    byTimeStats[idx].GCHeapSizeMB = Math.Max(byTimeStats[idx].GCHeapSizeMB, totalSize / 1000000.0F);
                }
            };

            dispatcher.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += delegate (ThreadPoolWorkerThreadAdjustmentTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    // TODO compute the average weighted by time.  
                    byTimeStats[idx].ThreadPoolThreadCountSum += data.NewWorkerThreadCount;
                    byTimeStats[idx].ThreadPoolAdjustmentCount++;
                }
            };

            var lastDiskEndMSec = new GrowableArray<double>(4);
            dispatcher.Kernel.AddCallbackForEvents<DiskIOTraceData>(delegate (DiskIOTraceData data)
            {
                // Compute the disk service time.  
                if (data.DiskNumber >= lastDiskEndMSec.Count)
                {
                    lastDiskEndMSec.Count = data.DiskNumber + 1;
                }

                var elapsedMSec = data.ElapsedTimeMSec;
                double serviceTimeMSec = elapsedMSec;
                double durationSinceLastIOMSec = data.TimeStampRelativeMSec - lastDiskEndMSec[data.DiskNumber];
                if (durationSinceLastIOMSec < serviceTimeMSec)
                {
                    serviceTimeMSec = durationSinceLastIOMSec;
                }

                // Add it to the stats.  
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].DiskIOMsec += serviceTimeMSec;
                }
            });

            dispatcher.Kernel.ThreadCSwitch += delegate (CSwitchTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].ContextSwitch++;
                }
            };

            aspNet.AspNetReqStart += delegate (AspNetStartTraceData data)
            {
                var request = new AspNetRequest();
                request.ID = data.ContextId;
                request.Path = data.Path;
                request.Method = data.Method;
                request.QueryString = data.QueryString;
                request.StartTimeRelativeMSec = data.TimeStampRelativeMSec;
                request.StartThreadID = data.ThreadID;
                request.Process = data.Process();

                requestByID[request.ID] = request;
                m_requests.Add(request);

                requestsRecieved++;
                request.RequestsReceived = requestsRecieved;
                request.RequestsProcessing = requestsProcessing;
            };

            aspNet.AspNetReqStop += delegate (AspNetStopTraceData data)
            {
                AspNetRequest request;
                if (requestByID.TryGetValue(data.ContextId, out request))
                {
                    // If we missed the hander end, then complete it.  
                    if (request.HandlerStartTimeRelativeMSec > 0 && request.HandlerStopTimeRelativeMSec == 0)
                    {
                        --requestsProcessing;
                        request.HandlerStopTimeRelativeMSec = data.TimeStampRelativeMSec;
                        Debug.Assert(requestsProcessing >= 0);
                    }

                    Debug.Assert(request.StopTimeRelativeMSec == 0);
                    request.StopTimeRelativeMSec = data.TimeStampRelativeMSec;
                    request.StopThreadID = data.ThreadID;
                    Debug.Assert(request.StopTimeRelativeMSec > request.StartTimeRelativeMSec);

                    --requestsRecieved;
                    Debug.Assert(requestsRecieved >= 0);

                    int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                    if (idx >= 0)
                    {
                        var byTimeState = byTimeStats[idx];
                        byTimeState.NumRequests++;
                        byTimeState.DurationMSecTotal += (float)request.DurationMSec;
                        byTimeState.QueuedDurationMSecTotal += (float)request.QueueDurationMSec;
                        if ((float)request.DurationMSec > byTimeState.RequestsMSecMax)
                        {
                            byTimeState.RequestsThreadOfMax = request.HandlerThreadID;
                            byTimeState.RequestsTimeOfMax = request.StartTimeRelativeMSec;
                            byTimeState.RequestsMSecMax = (float)request.DurationMSec;
                        }
                    }
                }
                else
                {
                    log.WriteLine("WARNING: stop event without a start at {0:n3} Msec.", data.TimeStampRelativeMSec);
                }
            };


            Action<int, double, Guid> handlerStartAction = delegate (int threadID, double timeStampRelativeMSec, Guid contextId)
            {
                AspNetRequest request;
                if (requestByID.TryGetValue(contextId, out request))
                {
                    // allow this routine to be called twice for the same event.  
                    if (request.HandlerStartTimeRelativeMSec != 0)
                    {
                        return;
                    }

                    Debug.Assert(request.StopTimeRelativeMSec == 0);

                    request.HandlerStartTimeRelativeMSec = timeStampRelativeMSec;
                    request.HandlerThreadID = threadID;

                    requestsProcessing++;
                    Debug.Assert(requestsProcessing <= requestsRecieved);
                }
            };

            aspNet.AspNetReqStartHandler += delegate (AspNetStartHandlerTraceData data)
            {
                handlerStartAction(data.ThreadID, data.TimeStampRelativeMSec, data.ContextId);
            };

            // When you don't turn on the most verbose ASP.NET events, you only get a SessionDataBegin event.  Use
            // this as the start of the processing (because it is pretty early) if we have nothing better to use.  
            aspNet.AspNetReqSessionDataBegin += delegate (AspNetAcquireSessionBeginTraceData data)
            {
                handlerStartAction(data.ThreadID, data.TimeStampRelativeMSec, data.ContextId);
            };

            aspNet.AspNetReqEndHandler += delegate (AspNetEndHandlerTraceData data)
            {
                AspNetRequest request;
                if (requestByID.TryGetValue(data.ContextId, out request))
                {
                    if (request.HandlerStartTimeRelativeMSec > 0 && request.HandlerStopTimeRelativeMSec == 0)            // If we saw the start 
                    {
                        --requestsProcessing;
                        request.HandlerStopTimeRelativeMSec = data.TimeStampRelativeMSec;
                    }
                    Debug.Assert(requestsProcessing >= 0);
                }
            };

            dispatcher.Process();
            requestByID = null;         // We are done with the table

            var globalMaxRequestsReceived = 0;
            var globalMaxRequestsQueued = 0;
            var globalMaxRequestsProcessing = 0;

            // It is not uncommon for there to be missing end events, etc, which mess up the running counts of 
            // what is being processed.   Thus look for these messed up events and fix them.  Once the counts
            // are fixed use them to compute the number queued and number being processed over the interval.  
            int recAdjust = 0;
            int procAdjust = 0;
            foreach (var req in m_requests)
            {
                // Compute the fixup for the all subsequent request.  
                Debug.Assert(0 < req.StartTimeRelativeMSec);         // we always set the start time. 

                // Throw out receive counts that don't have a end event
                if (req.StopTimeRelativeMSec == 0)
                {
                    recAdjust++;
                }

                // Throw out process counts that don't have a stop handler or a stop.   
                if (0 < req.HandlerStartTimeRelativeMSec && (req.HandlerStopTimeRelativeMSec == 0 || req.StopTimeRelativeMSec == 0))
                {
                    procAdjust++;
                }

                // Fix up the requests 
                req.RequestsReceived -= recAdjust;
                req.RequestsProcessing -= procAdjust;

                Debug.Assert(0 <= req.RequestsReceived);
                Debug.Assert(0 <= req.RequestsProcessing);
                Debug.Assert(0 <= req.RequestsQueued);
                Debug.Assert(req.RequestsQueued <= req.RequestsReceived);

                // A this point req is accurate.   Calcuate global and byTime stats from that.  
                if (globalMaxRequestsReceived < req.RequestsReceived)
                {
                    globalMaxRequestsReceived = req.RequestsReceived;
                }

                if (globalMaxRequestsProcessing < req.RequestsProcessing)
                {
                    globalMaxRequestsProcessing = req.RequestsProcessing;
                }

                var requestsQueued = req.RequestsQueued;
                if (globalMaxRequestsQueued < requestsQueued)
                {
                    globalMaxRequestsQueued = requestsQueued;
                }

                if (req.StopTimeRelativeMSec > 0)
                {
                    int idx = GetBucket(req.StopTimeRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                    if (idx >= 0)
                    {
                        byTimeStats[idx].MinRequestsQueued = Math.Min(byTimeStats[idx].MinRequestsQueued, requestsQueued);
                        byTimeStats[idx].MeanRequestsProcessingSum += req.RequestsProcessing;
                        byTimeStats[idx].MeanRequestsProcessingCount++;
                    }
                }
            }
            if (recAdjust != 0)
            {
                log.WriteLine("There were {0} event starts without a matching event end in the trace", recAdjust);
            }

            if (procAdjust != 0)
            {
                log.WriteLine("There were {0} handler starts without a matching handler end in the trace", procAdjust);
            }

            writer.WriteLine("<H2>ASP.Net Statistics</H2>");
            writer.WriteLine("<UL>");
            var fileInfo = new System.IO.FileInfo(dataFile.FilePath);
            writer.WriteLine("<LI> Total Requests: {0:n} </LI>", m_requests.Count);
            writer.WriteLine("<LI> Trace Duration (Sec): {0:n1} </LI>", dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> Average Request/Sec: {0:n2} </LI>", m_requests.Count / dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> Number of CPUs: {0}</LI>", dataFile.NumberOfProcessors);
            writer.WriteLine("<LI> Maximum Number of requests recieved but not replied to: {0}</LI>", globalMaxRequestsReceived);
            writer.WriteLine("<LI> Maximum Number of requests queued waiting for processing: {0}</LI>", globalMaxRequestsQueued);
            writer.WriteLine("<LI> Maximum Number of requests concurrently being worked on: {0}</LI>", globalMaxRequestsProcessing);
            writer.WriteLine("<LI> Total Memory (Meg): {0:n0}</LI>", dataFile.MemorySizeMeg);
            writer.WriteLine("<LI> GC Kind: {0} </LI>", GCType);
            writer.WriteLine("<LI> <A HREF=\"#rollupPerTime\">Rollup over time</A></LI>");
            writer.WriteLine("<LI> <A HREF=\"#rollupPerRequestType\">Rollup per request type</A></LI>");
            writer.WriteLine("<LI> <A HREF=\"command:excel/requests\">View ALL individual requests in Excel</A></LI>");
            writer.WriteLine("</UL>");

            writer.Write("<P><A ID=\"rollupPerTime\">Statistics over time.  Hover over column headings for explaination of columns.</A></P>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Time Interval MSec</TH>");
            writer.Write("<TH Align=\"Center\">Req/Sec</TH>");
            writer.Write("<TH Align=\"Center\">Max Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The start time of the maximum response (may preceed bucket start)\">Start of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\">Thread of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The time from when the response is read from the OS until we have written a reply.\">Mean Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The time a request waits before processing begins.\">Mean Queue<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The minium number of requests that have been recieved but not yet processed.\">Min<BR>Queued</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The average number of requests that are actively being processed simultaneously.\">Mean<BR>Proc</TH>");
            writer.Write("<TH Align=\"Center\">CPU %</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of context switches per second.\">CSwitch / Sec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The total amount of time (MSec) the disk was active (all disks), machine wide.\">Disk<BR>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The average number of thread-pool worker over this time period\">Thread<BR>Workers</TH>");
            writer.Write("<TH Align=\"Center\">GC Alloc<BR/>MB/Sec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The maximum of the GC heap size (in any process) after any GC\">GC Heap<BR/>MB</TH>");
            writer.Write("<TH Align=\"Center\">GCs</TH>");
            writer.Write("<TH Align=\"Center\">Gen2<BR/>GCs</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of times one thread had to wait for another thread because of a .NET lock\">.NET<BR/>Contention</TH>");
            writer.WriteLine("</TR>");

            // Rollup by time 

            // Only print until CPU goes to 0.  This is because the kernel events stop sooner, and it is confusing 
            // to have one without the other 
            var limit = numBuckets;
            while (0 < limit && byTimeStats[limit - 1].CpuMSec == 0)
            {
                --limit;
            }

            if (limit == 0)             // Something went wrong (e.g no CPU sampling turned on), give up on trimming.
            {
                limit = numBuckets;
            }

            bool wroteARow = false;
            for (int i = 0; i < limit; i++)
            {
                var byTimeStat = byTimeStats[i];
                if (byTimeStat.NumRequests == 0 && !wroteARow)       // Skip initial cases if any. 
                {
                    continue;
                }

                wroteARow = true;
                var startBucketMSec = startIntervalMSec + i * bucketIntervalMSec;
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Center\">{0:n0} - {1:n0}</TD>", startBucketMSec, startBucketMSec + bucketIntervalMSec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.NumRequests / (bucketIntervalMSec / 1000.0));
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.RequestsMSecMax);
                writer.Write("<TD Align=\"Center\">{0:n3}</TD>", byTimeStat.RequestsTimeOfMax);
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.RequestsThreadOfMax);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.DurationMSecTotal / byTimeStat.NumRequests);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.QueuedDurationMSecTotal / byTimeStat.NumRequests);
                writer.Write("<TD Align=\"Center\">{0}</TD>", (byTimeStat.MinRequestsQueued == int.MaxValue) ? 0 : byTimeStat.MinRequestsQueued - 1);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.MeanRequestsProcessing);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", byTimeStat.CpuMSec * 100.0 / (dataFile.NumberOfProcessors * bucketIntervalMSec));
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", byTimeStat.ContextSwitch / (bucketIntervalMSec / 1000.0));
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.DiskIOMsec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.MeanThreadPoolThreads);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.GCHeapAllocMB / (bucketIntervalMSec / 1000.0));
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.GCHeapSizeMB == 0 ? "No GCs" : byTimeStat.GCHeapSizeMB.ToString("f3"));
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.NumGcs);
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.NumGen2Gcs);
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.Contentions);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");

            var byRequestType = new Dictionary<string, ByRequestStats>();
            foreach (var request in m_requests)
            {
                // Skip requests that did not finish.  
                if (request.StopTimeRelativeMSec == 0)
                {
                    continue;
                }

                var key = request.Method + request.Path + request.QueryString;
                ByRequestStats stats;
                if (!byRequestType.TryGetValue(key, out stats))
                {
                    byRequestType.Add(key, new ByRequestStats(request));
                }
                else
                {
                    stats.AddRequest(request);
                }
            }

            var requestStats = new List<ByRequestStats>(byRequestType.Values);
            requestStats.Sort(delegate (ByRequestStats x, ByRequestStats y)
            {
                return -x.TotalDurationMSec.CompareTo(y.TotalDurationMSec);
            });

            // Rollup by kind of kind of page request
            writer.Write("<P><A ID=\"rollupPerRequestType\">Statistics Per Request URL</A></P>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Method</TH>");
            writer.Write("<TH Align=\"Center\">Path</TH>");
            writer.Write("<TH Align=\"Center\">Query String</TH>");
            writer.Write("<TH Align=\"Center\">Num</TH>");
            writer.Write("<TH Align=\"Center\">Num<BR/>&gt; 1s</TH>");
            writer.Write("<TH Align=\"Center\">Num<BR/>&gt; 5s</TH>");
            writer.Write("<TH Align=\"Center\">Num<BR/>&gt; 10s</TH>");
            writer.Write("<TH Align=\"Center\">Total<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\">Mean Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\">Max Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\">Start of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\">End of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\">Thread of<BR/>Max</TH>");
            writer.WriteLine("</TR>");

            foreach (var requestStat in requestStats)
            {
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Center\">{0}</TD>", requestStat.MaxRequest.Method);
                writer.Write("<TD Align=\"Center\">{0}</TD>", requestStat.MaxRequest.Path);
                var queryString = requestStat.MaxRequest.QueryString;
                if (string.IsNullOrWhiteSpace(queryString))
                {
                    queryString = "&nbsp;";
                }

                writer.Write("<TD Align=\"Center\">{0}</TD>", queryString);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequests);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequest1Sec);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequest5Sec);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequest10Sec);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.TotalDurationMSec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", requestStat.MeanRequestMSec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", requestStat.MaxRequest.DurationMSec);
                writer.Write("<TD Align=\"Center\">{0:n3}</TD>", requestStat.MaxRequest.StartTimeRelativeMSec);
                writer.Write("<TD Align=\"Center\">{0:n3}</TD>", requestStat.MaxRequest.StopTimeRelativeMSec);
                writer.Write("<TD Align=\"Center\">{0}</TD>", requestStat.MaxRequest.HandlerThreadID);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");
            // create some whitespace at the end 
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
        }

        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("excel/"))
            {
                var rest = command.Substring(6);
                if (rest == "requests")
                {
                    var csvFile = CacheFiles.FindFile(FilePath, ".aspnet.requests.csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        CreateCSVFile(m_requests, csvFile);
                    }
                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV file on " + csvFile;
                }
            }
            return "Unknown command " + command;
        }

        #region private
        private class AspNetRequest
        {
            public TraceProcess Process;
            public double DurationMSec { get { return Math.Max(StopTimeRelativeMSec - StartTimeRelativeMSec, 0); } }

            public double QueueDurationMSec
            {
                get
                {
                    // Missing Handler events can cause this.  Typically they are the first events in the system.
                    // TODO is this too misleading?  
                    if (!(HandlerStartTimeRelativeMSec >= StartTimeRelativeMSec))
                    {
                        return 0;
                    }

                    return HandlerStartTimeRelativeMSec - StartTimeRelativeMSec;
                }
            }
            public int StartThreadID;       // TODO remove?  not clear it is interesting. 
            public double StartTimeRelativeMSec;

            public int StopThreadID;        // TODO remove?  not clear it is interesting. 
            public double StopTimeRelativeMSec;

            public int HandlerThreadID;
            public double HandlerStartTimeRelativeMSec;
            public double HandlerStopTimeRelativeMSec;
            public double HandlerDurationMSec { get { return HandlerStopTimeRelativeMSec - HandlerStartTimeRelativeMSec; } }

            public int RequestsReceived;            // just after this request was received, how many have we received but not replied to?
            public int RequestsProcessing;          // just after this request was received, how many total requests are being processed.  
            public int RequestsQueued { get { return RequestsReceived - RequestsProcessing; } }

            public string Method;       // GET or POST
            public string Path;         // url path
            public string QueryString;  // Query 
            public Guid ID;
        }

        private class ByTimeRequestStats
        {
            public ByTimeRequestStats()
            {
                MinRequestsQueued = int.MaxValue;
            }
            public int NumRequests;
            public int CpuMSec;
            public int ContextSwitch;
            public double DiskIOMsec;         // The amount of Disk service time (all disks, machine wide).  

            public float RequestsMSecMax;
            public double RequestsTimeOfMax;
            public int RequestsThreadOfMax;

            public float DurationMSecTotal;
            public float QueuedDurationMSecTotal;

            public int ThreadPoolThreadCountSum;
            public int ThreadPoolAdjustmentCount;
            public float MeanThreadPoolThreads { get { return (float)ThreadPoolThreadCountSum / ThreadPoolAdjustmentCount; } }

            public float GCHeapAllocMB;
            public float GCHeapSizeMB;
            public float NumGcs;
            public float NumGen2Gcs;
            public int Contentions;
            public int MinRequestsQueued;
            public float MeanRequestsProcessing { get { return MeanRequestsProcessingSum / MeanRequestsProcessingCount; } }

            internal float MeanRequestsProcessingSum;
            internal int MeanRequestsProcessingCount;
        };

        private class ByRequestStats
        {
            public ByRequestStats(AspNetRequest request)
            {
                MaxRequest = request;
                AddRequest(request);
            }
            public void AddRequest(AspNetRequest request)
            {
                if (request.DurationMSec > MaxRequest.DurationMSec)
                {
                    MaxRequest = request;
                }

                TotalDurationMSec += request.DurationMSec;
                Debug.Assert(request.DurationMSec >= 0);
                Debug.Assert(TotalDurationMSec >= 0);
                NumRequests++;
                if (request.DurationMSec > 1000)
                {
                    NumRequest1Sec++;
                }

                if (request.DurationMSec > 5000)
                {
                    NumRequest5Sec++;
                }

                if (request.DurationMSec > 10000)
                {
                    NumRequest10Sec++;
                }
            }
            public double MeanRequestMSec { get { return TotalDurationMSec / NumRequests; } }

            public int NumRequest1Sec;
            public int NumRequest5Sec;
            public int NumRequest10Sec;

            public AspNetRequest MaxRequest;
            public double TotalDurationMSec;
            public int NumRequests;
        }

        private static int GetBucket(double timeStampMSec, int startIntervalMSec, int bucketIntervalMSec, int maxBucket)
        {
            if (timeStampMSec < startIntervalMSec)
            {
                return -1;
            }

            int idx = (int)(timeStampMSec / bucketIntervalMSec);
            if (idx >= maxBucket)
            {
                return -1;
            }

            return idx;
        }


        private void CreateCSVFile(List<AspNetRequest> requests, string csvFileName)
        {
            using (var csvFile = File.CreateText(csvFileName))
            {
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
                csvFile.WriteLine("Method{0}Path{0}QueryString{0}StartMSec{0}DurationMSec{0}ProcStartMSec{0}ProcessingMSec{0}ProcessID{0}ProcThread{0}Received{0}Processing{0}Queued", listSeparator);
                foreach (var request in requests)
                {
                    if (request.StopTimeRelativeMSec == 0)       // Skip incomplete entries
                    {
                        continue;
                    }

                    csvFile.WriteLine("{1}{0}{2}{0}{3}{0}{4:f3}{0}{5:f2}{0}{6:f3}{0}{7:f2}{0}{8}{0}{9}{0}{10}{0}{11}{0}{12}", listSeparator,
                        request.Method, EventWindow.EscapeForCsv(request.Path, ","), EventWindow.EscapeForCsv(request.QueryString, ","),
                        request.StartTimeRelativeMSec, request.DurationMSec, request.HandlerStartTimeRelativeMSec, request.HandlerDurationMSec,
                        (request.Process != null) ? request.Process.ProcessID : 0, request.HandlerThreadID, request.RequestsReceived,
                        request.RequestsProcessing, request.RequestsQueued);
                }
            }
        }

        private List<AspNetRequest> m_requests;
        #endregion
    }
}