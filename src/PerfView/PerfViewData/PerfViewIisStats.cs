using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.IIS_Trace;
using Microsoft.Diagnostics.Tracing.Stacks;
using PerfViewExtensibility;

namespace PerfView.PerfViewData
{
    public class PerfViewIisStats : PerfViewHtmlReport
    {
        private Dictionary<Guid, IisRequest> m_Requests = new Dictionary<Guid, IisRequest>();
        private List<ExceptionDetails> allExceptions = new List<ExceptionDetails>();

        public PerfViewIisStats(PerfViewFile dataFile) : base(dataFile, "IIS Stats") { }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            writer.WriteLine("<H2>IIS Request Statistics</H2>");

            var dispatcher = dataFile.Events.GetSource();

            dispatcher.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-ASPNET", "Request/Send", delegate (TraceEvent data)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(data.ActivityID, out request))
                {
                    if (data.RelatedActivityID != Guid.Empty)
                    {
                        request.RelatedActivityId = data.RelatedActivityID;
                    }
                }
            });

            Dictionary<Guid, int> childRequests = new Dictionary<Guid, int>();

            var iis = new IisTraceEventParser(dispatcher);

            int startcount = 0;
            int endcount = 0;

            iis.IISGeneralGeneralChildRequestStart += delegate (W3GeneralChildRequestStart traceEvent)
            {
                int childRequestRecurseLevel = 0;
                if (childRequests.ContainsKey(traceEvent.ContextId))
                {
                    if (childRequests.TryGetValue(traceEvent.ContextId, out childRequestRecurseLevel))
                    {
                        childRequests[traceEvent.ContextId] = childRequestRecurseLevel + 1;
                    }
                }
                else
                {
                    childRequests.Add(traceEvent.ContextId, 1);
                }

            };

            iis.IISGeneralGeneralChildRequestEnd += delegate (W3GeneralChildRequestEnd traceEvent)
            {
                int childRequestRecurseLevel = 0;
                if (childRequests.ContainsKey(traceEvent.ContextId))
                {
                    if (childRequests.TryGetValue(traceEvent.ContextId, out childRequestRecurseLevel))
                    {
                        childRequests[traceEvent.ContextId] = childRequestRecurseLevel - 1;
                    }
                }
            };

            iis.IISGeneralGeneralRequestStart += delegate (W3GeneralStartNewRequest request)
            {

                IisRequest req = new IisRequest();
                req.ContextId = request.ContextId;
                req.StartTimeRelativeMSec = request.TimeStampRelativeMSec;
                req.Method = request.RequestVerb;
                req.Path = request.RequestURL;

                // This check is required for requests which have child
                // request events in them. For those, the StartNewRequest 
                // would be called twice for the same request. At this 
                // point, I don't think that is causing any problems to us
                if (!m_Requests.ContainsKey(request.ContextId))
                {
                    m_Requests.Add(request.ContextId, req);
                }

                startcount++;


            };

            iis.IISGeneralGeneralRequestEnd += delegate (W3GeneralEndNewRequest req)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(req.ContextId, out request))
                {
                    request.EndTimeRelativeMSec = req.TimeStampRelativeMSec;
                    request.BytesReceived = req.BytesReceived;
                    request.BytesSent = req.BytesSent;
                    request.StatusCode = req.HttpStatus;
                    request.SubStatusCode = req.HttpSubStatus;

                }

                endcount++;
            };


            iis.IISRequestNotificationPreBeginRequestStart += delegate (IISRequestNotificationPreBeginStart preBeginEvent)
            {
                IisRequest request;
                if (!m_Requests.TryGetValue(preBeginEvent.ContextId, out request))
                {
                    // so this is the case where we dont have a GENERAL_REQUEST_START 
                    // event but we got a MODULE\START Event fired for this request 
                    // so we do our best to create a FAKE start request event
                    // populating as much information as we can as this is one of 
                    // those requests which could have started before the trace was started
                    request = GenerateFakeIISRequest(preBeginEvent.ContextId, preBeginEvent);
                    m_Requests.Add(preBeginEvent.ContextId, request);
                }
                int childRequestRecurseLevel = GetChildEventRecurseLevel(preBeginEvent.ContextId, childRequests);

                var iisPrebeginModuleEvent = new IisPrebeginModuleEvent();
                iisPrebeginModuleEvent.Name = preBeginEvent.ModuleName;
                iisPrebeginModuleEvent.StartTimeRelativeMSec = preBeginEvent.TimeStampRelativeMSec;
                iisPrebeginModuleEvent.ProcessId = preBeginEvent.ProcessID;
                iisPrebeginModuleEvent.StartThreadId = preBeginEvent.ThreadID;
                iisPrebeginModuleEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
                request.PipelineEvents.Add(iisPrebeginModuleEvent);

            };

            iis.IISRequestNotificationPreBeginRequestEnd += delegate (IISRequestNotificationPreBeginEnd preBeginEvent)
            {
                IisRequest request;
                int childRequestRecurseLevel = GetChildEventRecurseLevel(preBeginEvent.ContextId, childRequests);
                if (m_Requests.TryGetValue(preBeginEvent.ContextId, out request))
                {
                    var module = request.PipelineEvents.FirstOrDefault(m => m.Name == preBeginEvent.ModuleName && m.ChildRequestRecurseLevel == childRequestRecurseLevel);

                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = preBeginEvent.TimeStampRelativeMSec;
                        module.EndThreadId = preBeginEvent.ThreadID;
                    }
                }
                // so this is the case where we dont have a GENERAL_REQUEST_START 
                // event as well as Module Start event for the request but we got 
                // a Module End Event fired for this request. Assuming this happens, 
                // the worst we will miss is delay between this module end event
                // to the next module start event and that should ideally be very
                // less. Hence we don't need the else part for this condition
                //else { }  
            };

            iis.IISRequestNotificationNotifyModuleStart += delegate (IISRequestNotificationEventsStart moduleEvent)
            {
                IisRequest request;
                if (!m_Requests.TryGetValue(moduleEvent.ContextId, out request))
                {
                    // so this is the case where we dont have a GENERAL_REQUEST_START 
                    // event but we got a MODULE\START Event fired for this request 
                    // so we do our best to create a FAKE start request event
                    // populating as much information as we can as this is one of 
                    // those requests which could have started before the trace was started
                    request = GenerateFakeIISRequest(moduleEvent.ContextId, moduleEvent);
                    m_Requests.Add(moduleEvent.ContextId, request);
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(moduleEvent.ContextId, childRequests);
                var iisModuleEvent = new IisModuleEvent();
                iisModuleEvent.Name = moduleEvent.ModuleName;
                iisModuleEvent.StartTimeRelativeMSec = moduleEvent.TimeStampRelativeMSec;
                iisModuleEvent.ProcessId = moduleEvent.ProcessID;
                iisModuleEvent.StartThreadId = moduleEvent.ThreadID;
                iisModuleEvent.fIsPostNotification = moduleEvent.fIsPostNotification;
                iisModuleEvent.Notification = (RequestNotification)moduleEvent.Notification;
                iisModuleEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
                iisModuleEvent.foundEndEvent = false;
                request.PipelineEvents.Add(iisModuleEvent);
            };

            iis.IISRequestNotificationNotifyModuleEnd += delegate (IISRequestNotificationEventsEnd moduleEvent)
            {
                IisRequest request;
                int childRequestRecurseLevel = GetChildEventRecurseLevel(moduleEvent.ContextId, childRequests);
                if (m_Requests.TryGetValue(moduleEvent.ContextId, out request))
                {
                    IEnumerable<IisModuleEvent> iisModuleEvents = request.PipelineEvents.OfType<IisModuleEvent>();
                    var module = iisModuleEvents.FirstOrDefault(m => m.Name == moduleEvent.ModuleName && m.Notification == (RequestNotification)moduleEvent.Notification && m.fIsPostNotification == moduleEvent.fIsPostNotificationEvent && m.ChildRequestRecurseLevel == childRequestRecurseLevel && m.foundEndEvent == false);
                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = moduleEvent.TimeStampRelativeMSec;
                        module.EndThreadId = moduleEvent.ThreadID;
                        module.foundEndEvent = true;
                    }
                }

                // so this is the case where we dont have a GENERAL_REQUEST_START event as well 
                // as Module Start event for the request but we got a Module End Event fired for 
                // this request. Assuming this happens, the worst we will miss is delay between 
                // this module end event to the next module start event and that should ideally be 
                // less. Hence we don't need the else part for this condition

            };

            iis.IISRequestNotificationModuleSetResponseErrorStatus += delegate (IISRequestNotificationEventsResponseErrorStatus responseErrorStatusNotification)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(responseErrorStatusNotification.ContextId, out request))
                {
                    request.FailureDetails = new RequestFailureDetails();
                    request.FailureDetails.HttpReason = responseErrorStatusNotification.HttpReason;
                    request.FailureDetails.HttpStatus = responseErrorStatusNotification.HttpStatus;
                    request.FailureDetails.HttpSubStatus = responseErrorStatusNotification.HttpSubStatus;
                    request.FailureDetails.ModuleName = responseErrorStatusNotification.ModuleName;
                    request.FailureDetails.ErrorCode = responseErrorStatusNotification.ErrorCode;
                    request.FailureDetails.ConfigExceptionInfo = responseErrorStatusNotification.ConfigExceptionInfo;
                    request.FailureDetails.Notification = (RequestNotification)responseErrorStatusNotification.Notification;
                    request.FailureDetails.TimeStampRelativeMSec = responseErrorStatusNotification.TimeStampRelativeMSec;
                }
            };

            iis.IISGeneralGeneralFlushResponseStart += delegate (W3GeneralFlushResponseStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISGeneralGeneralFlushResponseEnd += delegate (W3GeneralFlushResponseEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISGeneralGeneralReadEntityStart += delegate (W3GeneralReadEntityStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISGeneralGeneralReadEntityEnd += delegate (W3GeneralReadEntityEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISCacheFileCacheAccessStart += delegate (W3CacheFileCacheAccessStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISCacheFileCacheAccessEnd += delegate (W3CacheFileCacheAccessEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCacheUrlCacheAccessStart += delegate (W3CacheURLCacheAccessStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISCacheUrlCacheAccessEnd += delegate (W3CacheURLCacheAccessEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISFilterFilterStart += delegate (W3FilterStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISFilterFilterEnd += delegate (W3FilterEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISAuthenticationAuthStart += delegate (W3AuthStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISAuthenticationAuthEnd += delegate (W3AuthEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCacheOutputCacheLookupStart += delegate (W3OutputCacheLookupStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCacheOutputCacheLookupEnd += delegate (W3OutputCacheLookupEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionDynamicCompressionStart += delegate (W3DynamicCompressionStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionDynamicCompressionEnd += delegate (W3DynamicCompressionEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionStaticCompressionStart += delegate (W3StaticCompressionStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionStaticCompressionEnd += delegate (W3StaticCompressionEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISFilterFilterPreprocHeadersStart += delegate (W3FilterPreprocStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISFilterFilterPreprocHeadersEnd += delegate (W3FilterPreprocEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            var aspNet = new AspNetTraceEventParser(dispatcher);

            // The logic used here is that delays between "AspNetTrace/AspNetReq/Start" and "AspNetTrace/AspNetReq/AppDomainEnter"
            // will be due to the delay introduced due to the CLR threadpool code based on how
            // ASP.NET code emits these events.
            aspNet.AspNetReqStart += delegate (AspNetStartTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "CLRThreadPoolQueue");
                }
            };
            aspNet.AspNetReqAppDomainEnter += delegate (AspNetAppDomainEnterTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "CLRThreadPoolQueue");
                }
            };

            aspNet.AspNetReqSessionDataBegin += delegate (AspNetAcquireSessionBeginTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "AspNetReqSessionData");
                }
            };
            aspNet.AspNetReqSessionDataEnd += delegate (AspNetAcquireSessionEndTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "AspNetReqSessionData");
                }
            };

            aspNet.AspNetReqPipelineModuleEnter += delegate (AspNetPipelineModuleEnterTraceData traceEvent)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out request))
                {

                    var aspnetPipelineModuleEvent = new AspNetPipelineModuleEvent()
                    {
                        Name = traceEvent.ModuleName,
                        ModuleName = traceEvent.ModuleName,
                        StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec,
                        ProcessId = traceEvent.ProcessID,
                        StartThreadId = traceEvent.ThreadID,
                    };
                    request.PipelineEvents.Add(aspnetPipelineModuleEvent);
                }

            };

            aspNet.AspNetReqPipelineModuleLeave += delegate (AspNetPipelineModuleLeaveTraceData traceEvent)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out request))
                {
                    IEnumerable<AspNetPipelineModuleEvent> aspnetPipelineModuleEvents = request.PipelineEvents.OfType<AspNetPipelineModuleEvent>();
                    var module = aspnetPipelineModuleEvents.FirstOrDefault(m => m.ModuleName == traceEvent.ModuleName && m.foundEndEvent == false);
                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                        module.EndThreadId = traceEvent.ThreadID;
                        module.foundEndEvent = true;
                    }
                }
            };
            // Lets look at the rest of Enter/Leave events in AspNetReq now.

            aspNet.AddCallbackForEvents(name => name.EndsWith("Enter"), null, (TraceEvent traceEvent) =>
            {

                // We are using AspNetReqAppDomainEnter to compute for ClrThreadPool so exclude that for now
                if (!traceEvent.OpcodeName.EndsWith("AppDomainEnter") && !traceEvent.OpcodeName.EndsWith("PipelineModuleEnter"))
                {
                    object contextObj = traceEvent.PayloadByName("ContextId");
                    if (contextObj != null && contextObj.GetType() == typeof(Guid))
                    {
                        Guid contextGuid = (Guid)contextObj;

                        IisRequest iisRequest;
                        if (m_Requests.TryGetValue(contextGuid, out iisRequest))
                        {
                            AddGenericStartEventToRequest(contextGuid, traceEvent, childRequests);
                        }

                    }
                }
            });

            aspNet.AddCallbackForEvents(name => name.EndsWith("Leave"), null, (TraceEvent traceEvent) =>
            {
                if (!traceEvent.OpcodeName.EndsWith("PipelineModuleLeave"))
                {
                    object contextObj = traceEvent.PayloadByName("ContextId");
                    if (contextObj != null && contextObj.GetType() == typeof(Guid))
                    {
                        Guid contextGuid = (Guid)contextObj;

                        IisRequest iisRequest;
                        if (m_Requests.TryGetValue(contextGuid, out iisRequest))
                        {
                            AddGenericStopEventToRequest(contextGuid, traceEvent, childRequests);
                        }
                    }
                }
            });

            var clr = new ClrTraceEventParser(dispatcher);

            clr.ExceptionStart += delegate (ExceptionTraceData data)
            {
                ExceptionDetails ex = new ExceptionDetails();
                ex.ExceptionMessage = data.ExceptionMessage;
                ex.ExceptionType = data.ExceptionType;
                ex.ThreadId = data.ThreadID;
                ex.ProcessId = data.ProcessID;
                ex.TimeStampRelativeMSec = data.TimeStampRelativeMSec;
                allExceptions.Add(ex);
            };

            dispatcher.Process();

            // manual fixup for incomplete requests
            foreach (var request in m_Requests.Values.Where(x => x.EndTimeRelativeMSec == 0))
            {
                // so these are all the requests for which we see a GENERAL_REQUEST_START and no GENERAL_REQUEST_END
                // for these it is safe to set the request.EndTimeRelativeMSec to the last timestamp in the trace
                // because that is pretty much the duration that the request is active for.

                request.EndTimeRelativeMSec = dataFile.SessionEndTimeRelativeMSec;

                // Also, for this request, lets first try to find a pipeline start event which doesnt have a pipeline                
                // stop event next to it. If we find, we just set the EndTimeRelativeMSec to the end of the trace
                var incompletePipeLineEvents = request.PipelineEvents.Where(m => m.EndTimeRelativeMSec == 0);

                if (incompletePipeLineEvents.Count() >= 1)
                {
                    foreach (var incompleteEvent in incompletePipeLineEvents)
                    {
                        incompleteEvent.EndTimeRelativeMSec = dataFile.SessionEndTimeRelativeMSec;
                    }
                    // not setting incompleteEvent.EndThreadId as this is incorrectly adding a hyperlink for requests
                    // requests that are stuck in the session state module
                }

            }

            writer.WriteLine("<UL>");
            var fileInfo = new System.IO.FileInfo(dataFile.FilePath);
            writer.WriteLine("<LI> Total Requests: {0:n} </LI>", m_Requests.Count);
            writer.WriteLine("<LI> Trace Duration (Sec): {0:n1} </LI>", dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> RPS (Requests/Sec): {0:n2} </LI>", m_Requests.Count / dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> Number of CPUs: {0}</LI>", dataFile.NumberOfProcessors);
            writer.WriteLine("<LI> Successful Requests: {0}</LI>", m_Requests.Values.Count(x => x.StatusCode < 400));
            writer.WriteLine("<LI> Failed Requests: {0}</LI>", m_Requests.Values.Count(x => x.StatusCode >= 400));
            writer.WriteLine("</UL>");

            try
            {
                writer.WriteLine("<H3>HTTP Statistics Per Request URL</H3>");

                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>");
                writer.Write("<TH Align='Center' Title='This is the Request URL. '>Path</TH>");
                writer.Write("<TH Align='Center' Title='All requests for this URL'>Total</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished with a Success HTTP Status Code'>HTTP Status (200-206)</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished with a Redirect HTTP Status Code'>HTTP Status (301-307)</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that failed with a HTTP Client error'>HTTP Status (400-412)</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that failed with a HTTP server error'>HTTP Status (>=500)</TH>");
                writer.WriteLine("</TR>");

                var httpStatisticsPerUrl = m_Requests.Values.GroupBy(n => n.Path.Split('?')[0]).Select(c => new { Path = c.Key, Total = c.Count(), Successful = c.Count(s => s.StatusCode <= 206), Redirect = c.Count(s => s.StatusCode >= 301 && s.StatusCode <= 307), ClientError = c.Count(s => s.StatusCode >= 400 && s.StatusCode <= 412), ServerError = c.Count(s => s.StatusCode >= 500) });

                // sort this list by the the maximum number of requests
                foreach (var item in httpStatisticsPerUrl.OrderByDescending(x => x.Total))
                {
                    writer.WriteLine("<TR>");
                    writer.Write($"<TD>{item.Path}</TD><TD Align='Center'>{item.Total}</TD><TD Align='Center'>{item.Successful}</TD><TD Align='Center'>{item.Redirect}</TD><TD Align='Center'>{item.ClientError}</TD><TD Align='Center'>{item.ServerError}</TD>");
                    writer.Write("</TR>");
                }
                writer.WriteLine("</Table>");
            }
            catch (Exception e)
            {
                log.WriteLine(@"Error while displaying 'HTTP Statistics Per Request URL' " + "\r\n" + e.ToString());
            }


            try
            {
                writer.WriteLine("<H3>HTTP Request Execution Statistics Per Request URL</H3>");

                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>");
                writer.Write("<TH Align='Center' Title='This is the Request URL.'>Path</TH>");
                writer.Write("<TH Align='Center' Title='All requests for this URL'>Total</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 1 second'> &lt; 1s </TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 5 seconds'>&lt; 5s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 15 seconds'>&lt; 15s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 30 seconds'>&lt; 30s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 60 seconds'>&lt; 60s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in more than 60 seconds'>&gt; 60s</TH>");
                writer.WriteLine("</TR>");

                var httpRequestExecutionPerUrl = m_Requests.Values.GroupBy(n => n.Path.Split('?')[0]).Select(c => new
                {
                    Path = c.Key,
                    Total = c.Count(),
                    OneSec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 1000),
                    FiveSec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 1000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 5000),
                    FifteenSec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 5000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 15000),
                    ThirtySec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 15000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 30000),
                    SixtySec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 30000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 60000),
                    MoreThanSixtySec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) > 60000)
                });

                // sort this list by the the maximum number of requests
                foreach (var item in httpRequestExecutionPerUrl.OrderByDescending(x => x.Total))
                {
                    writer.WriteLine("<TR>");
                    writer.Write($"<TD>{item.Path}</TD><TD Align='Center'>{item.Total}</TD><TD Align='Center'>{item.OneSec}</TD><TD Align='Center'>{item.FiveSec}</TD><TD Align='Center'>{item.FifteenSec}</TD><TD Align='Center'>{item.ThirtySec}</TD><TD Align='Center'>{item.SixtySec}</TD><TD Align='Center'>{item.MoreThanSixtySec}</TD>");
                    writer.Write("</TR>");
                }
                writer.WriteLine("</Table>");
            }
            catch (Exception e)
            {
                log.WriteLine(@"Error while displaying 'HTTP Request Execution Statistics Per Request URL' " + "\r\n" + e.ToString());
            }



            writer.WriteLine("<H3>Top 100 Slowest Request Statistics</H3>");
            writer.WriteLine("The below table shows the top 100 slowest requests in this trace. Requests completing within 100 milliseconds are ignored. Hover over column headings for explaination of columns. <BR/><BR/>");

            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align='Center' Title='This column represents the HTTP METHOD used to make the request'>Method</TH>");
            writer.Write("<TH Align='Center' Title='This is the Request URL. Click on the individual requests to see all ETW events for that request.' >Path</TH>");
            writer.Write("<TH Align='Center' Title='cs-bytes represents the total bytes sent by the client for this HTTP Request'>cs-bytes</TH>");
            writer.Write("<TH Align='Center' Title='sc-bytes represents the total bytes that the server sent for the HTTP Response'>sc-bytes</TH>");
            writer.Write("<TH Align='Center' Title='The HTTP Status Code and Substatus code that server sent for this HTTP request'>HttpStatus</TH>");
            writer.Write("<TH Align='Center' Title='The total time it took to execute the request on the server'>Duration(ms)</TH>");
            writer.Write("<TH Align='Center' Title='This is the slowest module in the IIS request processing pipeline. Click on the the slowest module to see user mode stack trace for the thread. Hyperlinks to open thread stack traces are added only if the starting thread and ending thread for the module are the same.'>Slowest Module</TH>");
            writer.Write("<TH Align='Center' Title='This represents the time spent in the slowest module (in milliseconds)'>Time Spent In Slowest Module(ms)</TH>");
            writer.Write("<TH Align='Center' Title='This column gives you a percentage of time spent in the slowest module to the total time spent in request execution'>%Time Spent In Slowest Module</TH>");
            writer.Write("<TH Align='Center' Title='Click on the relevant Views to see the stack traces captured for the request'>Stack Traces</TH>");
            writer.WriteLine("</TR>");



            foreach (var request in m_Requests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderByDescending(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).Take(100).Where((m => (m.EndTimeRelativeMSec - m.StartTimeRelativeMSec) > 100)))
            {
                writer.WriteLine("<TR>");
                double slowestTime = 0;
                IisPipelineEvent slowestPipelineEvent = GetSlowestEvent(request);
                slowestTime = slowestPipelineEvent.EndTimeRelativeMSec - slowestPipelineEvent.StartTimeRelativeMSec;

                int processId = slowestPipelineEvent.ProcessId;
                int ThreadId = slowestPipelineEvent.StartThreadId;

                double startTimePipelineEvent = slowestPipelineEvent.StartTimeRelativeMSec;
                double endTimePipelineEvent = slowestPipelineEvent.EndTimeRelativeMSec;

                string threadTimeStacksString = $"{processId};{ThreadId};{startTimePipelineEvent};{endTimePipelineEvent}";
                string activityStacksString = $"{processId};{request.RelatedActivityId.ToString()};{startTimePipelineEvent};{endTimePipelineEvent}";

                double totalTimeSpent = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

                string requestPath = request.Path;

                // limit display of URL to specific charachter length only otherwise the table is expanding crazily
                if (requestPath.Length > 85)
                {
                    requestPath = requestPath.Substring(0, 80) + "...";
                }

                string threadTimeStacks = "";
                string activityStacks = "";

                // limit display of even the module names to specific charachter length only otherwise the table is expanding crazily
                string slowestPipelineEventDisplay = slowestPipelineEvent.ToString();
                if (slowestPipelineEventDisplay.Length > 55)
                {
                    slowestPipelineEventDisplay = slowestPipelineEventDisplay.Substring(0, 50) + "...";
                }

                if (slowestPipelineEvent.StartThreadId == slowestPipelineEvent.EndThreadId)
                {
                    threadTimeStacks = $"<A HREF =\"command:threadtimestacks:{threadTimeStacksString}\">Thread Time stacks</A>";
                }
                else
                {
                    threadTimeStacks = "";
                }

                activityStacks = $"<A HREF =\"command:activitystacks:{activityStacksString}\">Activity Stacks</A>";

                string detailedRequestCommandString = $"detailedrequestevents:{request.ContextId};{request.StartTimeRelativeMSec};{request.EndTimeRelativeMSec}";

                string csBytes = (request.BytesReceived == 0) ? "-" : request.BytesReceived.ToString();
                string scBytes = (request.BytesSent == 0) ? "-" : request.BytesSent.ToString();
                string statusCode = (request.StatusCode == 0) ? "-" : $"{ request.StatusCode}.{ request.SubStatusCode}";

                writer.WriteLine($"<TD>{request.Method}</TD><TD><A HREF=\"command:{detailedRequestCommandString}\">{requestPath}</A></TD><TD>{csBytes}</TD><TD>{scBytes}</TD><TD>{statusCode}</TD><TD>{totalTimeSpent:0.00}</TD><TD>{slowestPipelineEventDisplay}</TD><TD>{slowestTime:0.00}</TD><TD>{((slowestTime / totalTimeSpent * 100)):0.00}%</TD><TD>{activityStacks} {threadTimeStacks}</TD>");
                writer.Write("</TR>");
            }
            writer.WriteLine("</TABLE>");

            if (m_Requests.Values.Count(x => x.FailureDetails != null) > 0)
            {

                writer.WriteLine("<H2>Failed Requests</H2>");

                writer.WriteLine("<BR/>The below table provides details of all the failed requests (requests with StatusCode >399) in the trace. <BR/>");

                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>");
                writer.Write("<TH Align='Center' Title='This column represents the HTTP METHOD used to make the request'>Method</TH>");
                writer.Write("<TH Align='Center' Title='This is the Request URL' >Path</TH>");
                writer.Write("<TH Align='Center' Title='cs-bytes represents the total bytes sent by the client for this HTTP Request'>cs-bytes</TH>");
                writer.Write("<TH Align='Center' Title='sc-bytes represents the total bytes that the server sent for the HTTP Response'>sc-bytes</TH>");
                writer.Write("<TH Align='Center' Title='The HTTP Status Code and Substatus code that server sent for this HTTP request'>HttpStatus</TH>");
                writer.Write("<TH Align='Center' Title='A user-friendly description of the error code sent by the server'>Reason</TH>");
                writer.Write("<TH Align='Center' Title='This is the actual HTTP Status which the server sent for this request irrespective of the failure'>Final Status</TH>");
                writer.Write("<TH Align='Center' Title='Additional error code that IIS generated for the failed request'>ErrorCode</TH>");
                writer.Write("<TH Align='Center' Title='The module reponsible for setting the failed HTTP Status' >FailingModuleName</TH>");
                writer.Write("<TH Align='Center' Title='The total time it took to execute the request on the server'>Duration(ms)</TH>");
                writer.Write("<TH Align='Center' Title='Any CLR Exceptions that happened on this thread'>Exceptions</TH>");
                writer.WriteLine("</TR>");

                foreach (var request in m_Requests.Values.Where(x => x.FailureDetails != null))
                {
                    writer.WriteLine("<TR>");


                    double totalTimeSpent = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

                    string requestPath = request.Path;

                    // limit display of URL to 100 charachters only
                    // otherwise the table is expanding crazily
                    if (requestPath.Length > 100)
                    {
                        requestPath = requestPath.Substring(0, 100) + "...";
                    }

                    string detailedRequestCommandString = $"detailedrequestevents:{request.ContextId};{request.StartTimeRelativeMSec};{request.EndTimeRelativeMSec}";

                    string csBytes = (request.BytesReceived == 0) ? "-" : request.BytesReceived.ToString();
                    string scBytes = (request.BytesSent == 0) ? "-" : request.BytesSent.ToString();

                    string exceptionDetails = FindExceptionForThisRequest(request);

                    writer.WriteLine($"<TD>{request.Method}</TD><TD><A HREF=\"command:{detailedRequestCommandString}\">{requestPath}</A></TD><TD>{csBytes}</TD><TD>{scBytes}</TD><TD>{request.FailureDetails.HttpStatus}.{request.FailureDetails.HttpSubStatus}</TD><TD>{request.FailureDetails.HttpReason}</TD><TD>{request.StatusCode}.{request.SubStatusCode}</TD><TD>{request.FailureDetails.ErrorCode}</TD><TD>{request.FailureDetails.ModuleName} ({request.FailureDetails.Notification}) </TD><TD>{totalTimeSpent:0.00}</TD><TD>{exceptionDetails}</TD>");

                    writer.Write("</TR>");
                }

                writer.WriteLine("</TABLE>");
            }
            else
            {
                writer.WriteLine("<BR/><BR/>There are no failed requests (HTTP Requests with StatusCode >=400) in this trace file");
            }

            writer.Flush();
        }

        private int GetChildEventRecurseLevel(Guid contextId, Dictionary<Guid, int> childRequests)
        {
            int childRequestRecurseLevel = 0;
            if (childRequests.ContainsKey(contextId))
            {
                childRequests.TryGetValue(contextId, out childRequestRecurseLevel);
            }
            return childRequestRecurseLevel;
        }

        private string FindExceptionForThisRequest(IisRequest request)
        {
            double startTimeForPipeLineEvent = 0;
            string exceptionMessage = "";
            int processId = 0;
            int threadId = 0;
            foreach (var item in request.PipelineEvents.OfType<IisModuleEvent>().Where(x => x.Name == request.FailureDetails.ModuleName))
            {
                var moduleEvent = item as IisModuleEvent;

                if (moduleEvent.Notification == request.FailureDetails.Notification)
                {
                    startTimeForPipeLineEvent = moduleEvent.StartTimeRelativeMSec;
                    processId = moduleEvent.ProcessId;
                    if (moduleEvent.StartThreadId == moduleEvent.EndThreadId)
                    {
                        threadId = moduleEvent.StartThreadId;
                    }
                }
            }

            Dictionary<string, int> exceptionsList = new Dictionary<string, int>();

            if (startTimeForPipeLineEvent > 0 && processId != 0 && threadId != 0)
            {

                foreach (var ex in allExceptions.Where(x => x.TimeStampRelativeMSec > startTimeForPipeLineEvent && x.TimeStampRelativeMSec <= request.FailureDetails.TimeStampRelativeMSec
                                                                                                                && processId == x.ProcessId
                                                                                                                && threadId == x.ThreadId))
                {
                    exceptionMessage = ex.ExceptionType + ":" + ex.ExceptionMessage;

                    if (exceptionsList.ContainsKey(exceptionMessage))
                    {
                        exceptionsList[exceptionMessage] = exceptionsList[exceptionMessage] + 1;
                    }
                    else
                    {
                        exceptionsList.Add(exceptionMessage, 1);
                    }
                }
            }

            string returnString = "";
            foreach (var item in exceptionsList.OrderByDescending(x => x.Value))
            {
                returnString = $"{item.Value}  exceptions [{item.Key.ToString()}] <br/>";
            }

            return returnString;
        }

        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("detailedrequestevents:"))
            {
                string detailedrequesteventsString = command.Substring(22);

                var detailedrequesteventsParams = detailedrequesteventsString.Split(';');

                if (detailedrequesteventsParams.Length > 2)
                {
                    string requestId = detailedrequesteventsParams[0];
                    string startTime = detailedrequesteventsParams[1];
                    string endTime = detailedrequesteventsParams[2];

                    var etlFile = new ETLDataFile(DataFile.FilePath);
                    var events = etlFile.Events;

                    // Pick out the desired events. 
                    var desiredEvents = new List<string>();
                    foreach (var eventName in events.EventNames)
                    {
                        if (eventName.Contains("IIS_Trace") || eventName.Contains("AspNet"))
                        {
                            desiredEvents.Add(eventName);
                        }
                    }
                    events.SetEventFilter(desiredEvents);

                    GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                    {
                        // TODO FIX NOW this is probably a hack?
                        var file = PerfViewFile.Get(events.m_EtlFile.FilePath);
                        var eventSource = new PerfViewEventSource(file);
                        eventSource.m_eventSource = events;

                        eventSource.Viewer = new EventWindow(GuiApp.MainWindow, eventSource);
                        eventSource.Viewer.TextFilterTextBox.Text = requestId;
                        eventSource.Viewer.StartTextBox.Text = startTime;
                        eventSource.Viewer.EndTextBox.Text = endTime;
                        eventSource.Viewer.Loaded += delegate
                        {
                            eventSource.Viewer.EventTypes.SelectAll();
                            eventSource.Viewer.Update();
                        };

                        eventSource.Viewer.Show();
                    });
                }
            }

            else if (command.StartsWith("threadtimestacks:"))
            {
                string threadTimeStacksString = command.Substring(17);

                var threadTimeStacksParams = threadTimeStacksString.Split(';');

                int processID = Convert.ToInt32(threadTimeStacksParams[0]);
                int threadId = Convert.ToInt32(threadTimeStacksParams[1]);
                string startTime = threadTimeStacksParams[2];
                string endTime = threadTimeStacksParams[3];

                using (var etlFile = CommandEnvironment.OpenETLFile(DataFile.FilePath))
                {
                    etlFile.SetFilterProcess(processID);
                    var stacks = etlFile.ThreadTimeStacks();
                    stacks.Filter.StartTimeRelativeMSec = startTime;
                    stacks.Filter.EndTimeRelativeMSec = endTime;

                    //Thread(38008); (11276)
                    stacks.Filter.IncludeRegExs = $"Process% w3wp ({processID.ToString()});Thread ({threadId.ToString()})";

                    CommandEnvironment.OpenStackViewer(stacks);

                }
            }
            else if (command.StartsWith("activitystacks:"))
            {
                string activityStacksString = command.Substring(16);

                var activityStacksParams = activityStacksString.Split(';');

                int processID = Convert.ToInt32(activityStacksParams[0]);
                string relatedActivityId = activityStacksParams[1];
                string startTime = activityStacksParams[2];
                string endTime = activityStacksParams[3];

                using (var etlFile = CommandEnvironment.OpenETLFile(DataFile.FilePath))
                {
                    var startStopSource = new MutableTraceEventStackSource(etlFile.TraceLog);

                    var computer = new ThreadTimeStackComputer(etlFile.TraceLog, App.GetSymbolReader(etlFile.TraceLog.FilePath))
                    {
                        UseTasks = true,
                        GroupByStartStopActivity = true,
                        ExcludeReadyThread = true
                    };
                    computer.GenerateThreadTimeStacks(startStopSource);

                    etlFile.SetFilterProcess(processID);
                    var stacks = new Stacks(startStopSource, "Thread Time (with StartStop Activities)", etlFile, false);

                    stacks.Filter.StartTimeRelativeMSec = startTime;
                    stacks.Filter.EndTimeRelativeMSec = endTime;
                    stacks.Filter.IncludeRegExs = relatedActivityId;
                    stacks.Filter.FoldRegExs = "ntoskrnl!%ServiceCopyEnd;System.Runtime.CompilerServices.Async%MethodBuilder;^STARTING TASK";

                    CommandEnvironment.OpenStackViewer(stacks);
                }
            }
            return base.DoCommand(command, worker);
        }

        #region private
        private IisRequest GenerateFakeIISRequest(Guid contextId, TraceEvent traceEvent, double timeStamp = 0)
        {
            IisRequest request = new IisRequest();
            request.ContextId = contextId;

            if (traceEvent != null)
            {
                request.StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
            }
            else
            {
                request.StartTimeRelativeMSec = timeStamp;
            }
            request.Method = "UNKNOWN";
            request.Path = "Unkwown (GENERAL_REQUEST_START event not captured in trace)";

            return request;
        }

        private void AddGenericStartEventToRequest(Guid contextId, TraceEvent traceEvent, Dictionary<Guid, int> childRequests, string pipelineEventName = "")
        {
            IisRequest request;

            if (!m_Requests.TryGetValue(contextId, out request))
            {
                // so this is the case where we dont have a GENERAL_REQUEST_START 
                // event but we got a Module Event fired for this request 
                // so we do our best to create a FAKE start request event
                // populating as much information as we can.
                request = GenerateFakeIISRequest(contextId, null, traceEvent.TimeStampRelativeMSec);
                m_Requests.Add(contextId, request);
            }


            var iisPipelineEvent = new IisPipelineEvent();
            if (string.IsNullOrEmpty(pipelineEventName))
            {
                if (traceEvent.OpcodeName.ToLower().EndsWith("_start"))
                {
                    iisPipelineEvent.Name = traceEvent.OpcodeName.Substring(0, traceEvent.OpcodeName.Length - 6);
                }
                // For All the AspnetReq events, they start with Enter or Begin
                // Also, we want to append the AspnetReq/ in front of them so we can easily distinguish them
                // as coming from ASP.NET pipeline
                else if (traceEvent.OpcodeName.ToLower().EndsWith("enter") || traceEvent.OpcodeName.ToLower().EndsWith("begin"))
                {
                    iisPipelineEvent.Name = traceEvent.EventName.Substring(0, traceEvent.EventName.Length - 5);
                }
            }
            else
            {
                iisPipelineEvent.Name = pipelineEventName;
            }

            int childRequestRecurseLevel = GetChildEventRecurseLevel(contextId, childRequests);
            iisPipelineEvent.StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
            iisPipelineEvent.StartThreadId = traceEvent.ThreadID;
            iisPipelineEvent.ProcessId = traceEvent.ProcessID;
            iisPipelineEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
            request.PipelineEvents.Add(iisPipelineEvent);

        }

        private void AddGenericStopEventToRequest(Guid contextId, TraceEvent traceEvent, Dictionary<Guid, int> childRequests, string pipelineEventName = "")
        {
            IisRequest request;
            if (m_Requests.TryGetValue(contextId, out request))
            {
                string eventName = "";

                if (string.IsNullOrEmpty(pipelineEventName))
                {
                    if (traceEvent.OpcodeName.ToLower().EndsWith("_end"))
                    {
                        eventName = traceEvent.OpcodeName.Substring(0, traceEvent.OpcodeName.Length - 4);
                    }

                    // For All the AspnetReq events, they finish with Leave. Also, we want to append the AspnetReq/ 
                    // in front of them so we can easily distinguish them as coming from ASP.NET pipeline
                    else if (traceEvent.OpcodeName.ToLower().EndsWith("leave"))
                    {
                        eventName = traceEvent.EventName.Substring(0, traceEvent.EventName.Length - 5);
                    }
                }
                else
                {
                    eventName = pipelineEventName;
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(contextId, childRequests);
                var iisPipelineEvent = request.PipelineEvents.FirstOrDefault(m => (m.Name == eventName) && m.EndTimeRelativeMSec == 0 && m.ChildRequestRecurseLevel == childRequestRecurseLevel);
                if (iisPipelineEvent != null)
                {
                    iisPipelineEvent.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    iisPipelineEvent.EndThreadId = traceEvent.ThreadID;
                }
            }
        }
        private IisPipelineEvent GetSlowestEvent(IisRequest request)
        {
            IisPipelineEvent slowestPipelineEvent = new IisPipelineEvent();
            double slowestTime = 0;

            foreach (var pipeLineEvent in request.PipelineEvents)
            {
                if (pipeLineEvent.StartTimeRelativeMSec != 0 && pipeLineEvent.EndTimeRelativeMSec != 0)
                {
                    var timeinThisEvent = pipeLineEvent.EndTimeRelativeMSec - pipeLineEvent.StartTimeRelativeMSec;
                    if (timeinThisEvent > slowestTime)
                    {
                        slowestTime = timeinThisEvent;
                        slowestPipelineEvent = pipeLineEvent;
                    }
                }
            }

            // Lets check for containment to see if a child event is taking more than 50% 
            // of the time of this pipeline event, then we want to call that out
            foreach (var pipeLineEvent in request.PipelineEvents.Where(x => (x.StartTimeRelativeMSec > slowestPipelineEvent.StartTimeRelativeMSec) && (x.EndTimeRelativeMSec <= slowestPipelineEvent.EndTimeRelativeMSec)))
            {
                var timeinThisEvent = pipeLineEvent.EndTimeRelativeMSec - pipeLineEvent.StartTimeRelativeMSec;

                if (((timeinThisEvent / slowestTime) * 100) > 50)
                {
                    slowestTime = timeinThisEvent;
                    slowestPipelineEvent = pipeLineEvent;
                }

            }

            var timeInSlowestEvent = slowestPipelineEvent.EndTimeRelativeMSec - slowestPipelineEvent.StartTimeRelativeMSec;
            var requestExecutionTime = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

            if (timeInSlowestEvent > 0 && requestExecutionTime > 500)
            {
                if (((timeInSlowestEvent / requestExecutionTime) * 100) < 50)
                {
                    // So this is the scenario where the default set of events that we are tracking
                    // do not have any delay. Lets do our best and see if we can at least
                    // populate the StartTime, EndTime                    

                    IisPipelineEvent unKnownPipeLineEvent = CheckForDelayInUnknownEvents(request, timeInSlowestEvent);

                    if (unKnownPipeLineEvent != null)
                    {
                        slowestPipelineEvent = unKnownPipeLineEvent;
                    }
                }
            }

            return slowestPipelineEvent;
        }

        private IisPipelineEvent CheckForDelayInUnknownEvents(IisRequest request, double timeInSlowestEvent)
        {
            double slowestTimeInThisEvent = 0;
            int position = 0;
            var pipelineEventsArray = request.PipelineEvents.ToArray();
            for (int i = 0; i < pipelineEventsArray.Length - 1; i++)
            {
                if (pipelineEventsArray[i].EndTimeRelativeMSec != 0)
                {
                    var timeDiff = pipelineEventsArray[i + 1].StartTimeRelativeMSec - pipelineEventsArray[i].EndTimeRelativeMSec;
                    if (slowestTimeInThisEvent < timeDiff)
                    {
                        slowestTimeInThisEvent = timeDiff;
                        position = i;
                    }
                }
            }

            IisPipelineEvent unknownEvent = null;

            if ((slowestTimeInThisEvent / timeInSlowestEvent) > 1.5)
            {
                if (position > 0)
                {
                    unknownEvent = new IisPipelineEvent();
                    unknownEvent.Name = "UNKNOWN";
                    unknownEvent.StartThreadId = pipelineEventsArray[position].EndThreadId;
                    unknownEvent.EndThreadId = pipelineEventsArray[position + 1].StartThreadId;
                    unknownEvent.StartTimeRelativeMSec = pipelineEventsArray[position].EndTimeRelativeMSec;
                    unknownEvent.EndTimeRelativeMSec = pipelineEventsArray[position + 1].StartTimeRelativeMSec;
                    unknownEvent.ProcessId = pipelineEventsArray[position + 1].ProcessId;
                }
            }

            return unknownEvent;
        }

        private class ExceptionDetails
        {
            public string ExceptionType;
            public string ExceptionMessage;
            public int ThreadId;
            public int ProcessId;
            public double TimeStampRelativeMSec;
        }

        private class IisRequest
        {
            public string Method;
            public string Path;
            public Guid ContextId;
            public int BytesSent;
            public int BytesReceived;
            public int StatusCode;
            public int SubStatusCode;
            public RequestFailureDetails FailureDetails;
            public double EndTimeRelativeMSec;
            public double StartTimeRelativeMSec;
            public List<IisPipelineEvent> PipelineEvents = new List<IisPipelineEvent>();
            public Guid RelatedActivityId;
        }

        private class IisPipelineEvent
        {
            public string Name;
            public int ProcessId;
            public int StartThreadId;
            public int EndThreadId;
            public double StartTimeRelativeMSec = 0;
            public double EndTimeRelativeMSec = 0;
            public int ChildRequestRecurseLevel = 0;
            public override string ToString()
            {
                return Name;
            }
        }

        private class AspNetPipelineModuleEvent : IisPipelineEvent
        {
            public string ModuleName;
            public bool foundEndEvent = false;

            public override string ToString()
            {
                return ModuleName;
            }
        }

        private class IisModuleEvent : IisPipelineEvent
        {
            public RequestNotification Notification;
            public bool fIsPostNotification;
            public bool foundEndEvent = false;

            public override string ToString()
            {
                return $"{Name} ({Notification.ToString()})";
            }
        }

        private class IisPrebeginModuleEvent : IisPipelineEvent
        {
            public override string ToString()
            {
                return $"{Name} (PreBegin)";
            }
        }

        private class RequestFailureDetails
        {
            public string ModuleName;
            public RequestNotification Notification;
            public string HttpReason;
            public int HttpStatus;
            public int HttpSubStatus;
            public int ErrorCode;
            public string ConfigExceptionInfo;
            public double TimeStampRelativeMSec;
        }

        #endregion 
    }
}