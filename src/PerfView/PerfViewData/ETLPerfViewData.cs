using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler;
using Microsoft.Diagnostics.Tracing.Parsers.InteropEventProvider;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Utilities;
using Utilities;
using EventSource = EventSources.EventSource;

namespace PerfView.PerfViewData
{
    public partial class ETLPerfViewData : PerfViewFile
    {
        public override string FormatName { get { return "ETW"; } }
        public override string[] FileExtensions { get { return new string[] { ".btl", ".etl", ".etlx", ".etl.zip", ".vspx" }; } }

        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            var traceLog = GetTraceLog(log);
            return new ETWEventSource(traceLog);
        }
        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            var eventLog = GetTraceLog(log);
            bool showOptimizationTiers =
                App.CommandLineArgs.ShowOptimizationTiers || streamName.Contains("(with Optimization Tiers)");
            if (streamName.StartsWith("CPU"))
            {
                return eventLog.CPUStacks(null, App.CommandLineArgs, showOptimizationTiers, predicate);
            }

            // var stackSource = new InternTraceEventStackSource(eventLog);
            var stackSource = new MutableTraceEventStackSource(eventLog);

            stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
            stackSource.ShowOptimizationTiers = showOptimizationTiers;

            TraceEvents events = eventLog.Events;
            if (!streamName.Contains("TaskTree") && !streamName.Contains("Tasks)"))
            {
                if (predicate != null)
                {
                    events = events.Filter(predicate);
                }
            }
            else
            {
                startRelativeMSec = 0;    // These require activity computers and thus need earlier events.   
            }

            if (startRelativeMSec != 0 || endRelativeMSec != double.PositiveInfinity)
            {
                events = events.FilterByTime(startRelativeMSec, endRelativeMSec);
            }

            var eventSource = events.GetSource();
            var sample = new StackSourceSample(stackSource);

            if (streamName == "Thread Time (with Tasks)")
            {
                return eventLog.ThreadTimeWithTasksStacks();
            }
            else if (streamName == "Thread Time (with ReadyThread)")
            {
                return eventLog.ThreadTimeWithReadyThreadStacks();
            }
            else if (streamName.StartsWith("ASP.NET Thread Time"))
            {
                if (streamName == "ASP.NET Thread Time (with Tasks)")
                {
                    return eventLog.ThreadTimeWithTasksAspNetStacks();
                }
                else
                {
                    return eventLog.ThreadTimeAspNetStacks();
                }
            }
            else if (streamName.StartsWith("Thread Time (with StartStop Activities)"))
            {
                // Handles the normal and (CPU ONLY) case
                var startStopSource = new MutableTraceEventStackSource(eventLog);

                var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
                computer.UseTasks = true;
                computer.GroupByStartStopActivity = true;
                computer.ExcludeReadyThread = true;
                computer.NoAwaitTime = streamName.Contains("(CPU ONLY)");
                computer.GenerateThreadTimeStacks(startStopSource);

                return startStopSource;
            }
            else if (streamName == "Thread Time")
            {
                return eventLog.ThreadTimeStacks();
            }
            else if (streamName == "Processes / Files / Registry")
            {
                return GetProcessFileRegistryStackSource(eventSource, log);
            }
            else if (streamName == "GC Heap Alloc Ignore Free")
            {
                var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                {
                    newHeap.OnObjectCreate += delegate (UInt64 objAddress, GCHeapSimulatorObject objInfo)
                    {
                        sample.Metric = objInfo.RepresentativeSize;
                        sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                               // We guess a count from the size.  
                        sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                        stackSource.AddSample(sample);
                        return true;
                    };
                };
                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName.StartsWith("GC Heap Net Mem"))
            {
                var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                if (streamName == "GC Heap Net Mem (Coarse Sampling)")
                {
                    gcHeapSimulators.UseOnlyAllocTicks = true;
                    m_extraTopStats = "Sampled only 100K bytes";
                }

                gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                {
                    newHeap.OnObjectCreate += delegate (UInt64 objAddress, GCHeapSimulatorObject objInfo)
                    {
                        sample.Metric = objInfo.RepresentativeSize;
                        sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                                // We guess a count from the size.  
                        sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                        stackSource.AddSample(sample);
                        return true;
                    };
                    newHeap.OnObjectDestroy += delegate (double time, int gen, UInt64 objAddress, GCHeapSimulatorObject objInfo)
                    {
                        sample.Metric = -objInfo.RepresentativeSize;
                        sample.Count = -(objInfo.RepresentativeSize / objInfo.Size);                                            // We guess a count from the size.  
                        sample.TimeRelativeMSec = time;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);       // We remove the same stack we added at alloc.  
                        stackSource.AddSample(sample);
                    };

                    newHeap.OnGC += delegate (double time, int gen)
                    {
                        sample.Metric = float.Epsilon;
                        sample.Count = 1;
                        sample.TimeRelativeMSec = time;
                        StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                        StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occured Gen(" + gen + ")");
                        sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                        stackSource.AddSample(sample);
                    };
                };
                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName.StartsWith("Gen 2 Object Deaths"))
            {
                var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);

                if (streamName == "Gen 2 Object Deaths (Coarse Sampling)")
                {
                    gcHeapSimulators.UseOnlyAllocTicks = true;
                    m_extraTopStats = "Sampled only 100K bytes";
                }

                gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                {
                    newHeap.OnObjectDestroy += delegate (double time, int gen, UInt64 objAddress, GCHeapSimulatorObject objInfo)
                    {
                        if (2 <= gen)
                        {
                            sample.Metric = objInfo.RepresentativeSize;
                            sample.Count = (objInfo.RepresentativeSize / objInfo.Size);                                         // We guess a count from the size.  
                            sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                            sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);
                            stackSource.AddSample(sample);
                        }
                    };

                    newHeap.OnGC += delegate (double time, int gen)
                    {
                        sample.Metric = float.Epsilon;
                        sample.Count = 1;
                        sample.TimeRelativeMSec = time;
                        StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                        StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occured Gen(" + gen + ")");
                        sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                        stackSource.AddSample(sample);
                    };
                };

                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName == "GC Heap Alloc Ignore Free (Coarse Sampling)")
            {
                TypeNameSymbolResolver typeNameSymbolResolver = new TypeNameSymbolResolver(FilePath, log);

                bool seenBadAllocTick = false;

                eventSource.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
                {
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                    var typeName = data.TypeName;
                    if (string.IsNullOrEmpty(typeName))
                    {
                        // Attempt to resolve the type name.
                        TraceLoadedModule module = data.Process().LoadedModules.GetModuleContainingAddress(data.TypeID, data.TimeStampRelativeMSec);
                        if (module != null)
                        {
                            // Resolve the type name.
                            typeName = typeNameSymbolResolver.ResolveTypeName((int)(data.TypeID - module.ModuleFile.ImageBase), module.ModuleFile, TypeNameSymbolResolver.TypeNameOptions.StripModuleName);
                        }
                    }

                    if (typeName != null && typeName.Length > 0)
                    {
                        var nodeIndex = stackSource.Interner.FrameIntern("Type " + typeName);
                        stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                    }

                    sample.Metric = data.GetAllocAmount(ref seenBadAllocTick);

                    if (data.AllocationKind == GCAllocationKind.Large)
                    {

                        var nodeIndex = stackSource.Interner.FrameIntern("LargeObject");
                        stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                    }

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
                m_extraTopStats = "Sampled only 100K bytes";
            }
            else if (streamName == "Exceptions")
            {
                eventSource.Clr.ExceptionStart += delegate (ExceptionTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    // Create a call stack that ends with the 'throw'
                    var nodeName = "Throw(" + data.ExceptionType + ") " + data.ExceptionMessage;
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    stackSource.AddSample(sample);
                };

                eventSource.Kernel.MemoryAccessViolation += delegate (MemoryPageFaultTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    // Create a call stack that ends with the 'throw'
                    var nodeName = "AccessViolation(ADDR=" + data.VirtualAddress.ToString("x") + ")";
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    stackSource.AddSample(sample);
                };

                eventSource.Process();
            }

            else if (streamName == "Pinning At GC Time")
            {
                // Wire up the GC heap simulations.  
                GCHeapSimulators gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);

                // Keep track of the current GC per process 
                var curGCGen = new int[eventLog.Processes.Count];
                var curGCIndex = new int[eventLog.Processes.Count];
                eventSource.Clr.GCStart += delegate (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData data)
                {
                    var process = data.Process();
                    if (process == null)
                    {
                        return;
                    }

                    curGCGen[(int)process.ProcessIndex] = data.Depth;
                    curGCIndex[(int)process.ProcessIndex] = data.Count;
                };

                // Keep track of the live Pinning handles per process.  
                var allLiveHandles = new Dictionary<UInt64, GCHandleInfo>[eventLog.Processes.Count];
                Action<SetGCHandleTraceData> onSetHandle = delegate (SetGCHandleTraceData data)
                {
                    if (!(data.Kind == GCHandleKind.AsyncPinned || data.Kind == GCHandleKind.Pinned))
                    {
                        return;
                    }

                    var process = data.Process();
                    if (process == null)
                    {
                        return;
                    }

                    var liveHandles = allLiveHandles[(int)process.ProcessIndex];
                    if (liveHandles == null)
                    {
                        allLiveHandles[(int)process.ProcessIndex] = liveHandles = new Dictionary<UInt64, GCHandleInfo>();
                    }

                    GCHandleInfo info;
                    var handle = data.HandleID;
                    if (!liveHandles.TryGetValue(handle, out info))
                    {
                        liveHandles[handle] = info = new GCHandleInfo();
                        info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;
                        info.ObjectAddress = data.ObjectID;
                        info.IsAsync = (data.Kind == GCHandleKind.AsyncPinned || data.Kind == GCHandleKind.DependendAsyncPinned);
                        info.GCGen = (byte)data.Generation;
                        info.PinStack = stackSource.GetCallStack(data.CallStackIndex(), data);

                        // watch this object as it GCs happen  (but frankly it should not move).  
                        gcHeapSimulators[process].TrackObject(info.ObjectAddress);
                    }
                };
                var clrPrivate = new ClrPrivateTraceEventParser(eventSource);
                clrPrivate.GCSetGCHandle += onSetHandle;
                eventSource.Clr.GCSetGCHandle += onSetHandle;

                Action<DestroyGCHandleTraceData> onDestroyHandle = delegate (DestroyGCHandleTraceData data)
                {
                    var process = data.Process();
                    if (process == null)
                    {
                        return;
                    }

                    var liveHandles = allLiveHandles[(int)process.ProcessIndex];
                    if (liveHandles == null)
                    {
                        allLiveHandles[(int)process.ProcessIndex] = liveHandles = new Dictionary<UInt64, GCHandleInfo>();
                    }

                    GCHandleInfo info;
                    var handle = data.HandleID;
                    if (liveHandles.TryGetValue(handle, out info))
                    {
                        liveHandles.Remove(handle);
                    }
                };
                clrPrivate.GCDestroyGCHandle += onDestroyHandle;
                eventSource.Clr.GCDestoryGCHandle += onDestroyHandle;

#if false 
                var cacheAllocated = new Dictionary<Address, bool>();
                Action<TraceEvent> onPinnableCacheAllocate = delegate(TraceEvent data) 
                {
                    var objectId = (Address) data.PayloadByName("objectId");
                    cacheAllocated[objectId] = true;
                };
                eventSource.Dynamic.AddCallbackForProviderEvent("AllocateBuffer", "Microsoft-DotNETRuntime-PinnableBufferCache", onPinnableCacheAllocate);
                eventSource.Dynamic.AddCallbackForProviderEvent("AllocateBuffer", "Microsoft-DotNETRuntime-PinnableBufferCache-Mscorlib", onPinnableCacheAllocate); 

                Action<PinPlugAtGCTimeTraceData> plugAtGCTime = delegate(PinPlugAtGCTimeTraceData data)
                {
                };
                clrPrivate.GCPinPlugAtGCTime += plugAtGCTime;
                eventSource.Clr.GCPinObjectAtGCTime += plugAtGCTime;
#endif
                // ThreadStacks maps locations in memory of the thread stack to and maps it to a thread.  
                var threadStacks = new Dictionary<UInt64, TraceThread>[eventLog.Processes.Count];

                // This per-thread information is used solely as a heuristic backstop to try to guess what
                // the Pinned handles are when we don't have other information.   We can remove it. 
                var lastHandleInfoForThreads = new PerThreadGCHandleInfo[eventLog.Threads.Count];

                // The main event, we have pinning that is happening at GC time.  
                Action<PinObjectAtGCTimeTraceData> objectAtGCTime = delegate (PinObjectAtGCTimeTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    var process = thread.Process;
                    var liveHandles = allLiveHandles[(int)process.ProcessIndex];
                    if (liveHandles == null)
                    {
                        allLiveHandles[(int)process.ProcessIndex] = liveHandles = new Dictionary<UInt64, GCHandleInfo>();
                    }

                    string pinKind = "UnknownPinned";
                    double pinStartTimeRelativeMSec = 0;
                    StackSourceCallStackIndex pinStack = StackSourceCallStackIndex.Invalid;
                    StackSourceCallStackIndex allocStack = StackSourceCallStackIndex.Invalid;
                    int gcGen = curGCGen[(int)process.ProcessIndex];
                    int gcIndex = curGCIndex[(int)process.ProcessIndex];

                    GCHandleInfo info;
                    if (liveHandles.TryGetValue(data.HandleID, out info))
                    {
                        pinStack = info.PinStack;
                        if (pinStack != StackSourceCallStackIndex.Invalid)
                        {
                            pinStartTimeRelativeMSec = info.PinStartTimeRelativeMSec;
                            pinKind = "HandlePinned";
                            gcGen = info.GCGen;
                        }
                        else if (data.ObjectID == info.ObjectAddress)
                        {
                            pinStartTimeRelativeMSec = info.PinStartTimeRelativeMSec;
                        }
                        else
                        {
                            info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;     // Restart trying to guess how long this lives
                            info.ObjectAddress = data.ObjectID;
                        }
                    }
                    else
                    {
                        liveHandles[data.HandleID] = info = new GCHandleInfo();
                        info.ObjectAddress = data.ObjectID;
                        info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;         // We guess the pinning started at this GC.  
                    }

                    // This is heuristic logic to determine if the pin handles are async or not. 
                    // Basically async handles are themselves pinned and then point at pinned things.  Thus
                    // if you see handles that point near other handles that is likely an async handle. 
                    // TODO I think we can remove this, because we no longer pin the async handle.  
                    if (pinStack == StackSourceCallStackIndex.Invalid)
                    {
                        var lastHandleInfo = lastHandleInfoForThreads[(int)thread.ThreadIndex];
                        if (lastHandleInfo == null)
                        {
                            lastHandleInfoForThreads[(int)thread.ThreadIndex] = lastHandleInfo = new PerThreadGCHandleInfo();
                        }

                        // If we see a handle that 
                        if (data.HandleID - lastHandleInfo.LikelyAsyncHandleTable1 < 128)
                        {
                            pinKind = "LikelyAsyncPinned";
                            lastHandleInfo.LikelyAsyncHandleTable1 = data.HandleID;
                        }
                        else if (data.HandleID - lastHandleInfo.LikelyAsyncHandleTable2 < 128)
                        {
                            // This is here for the async array of buffers case.   
                            pinKind = "LikelyAsyncPinned";
                            lastHandleInfo.LikelyAsyncHandleTable2 = lastHandleInfo.LikelyAsyncHandleTable1;
                            lastHandleInfo.LikelyAsyncHandleTable1 = data.HandleID;
                        }
                        if (data.HandleID - lastHandleInfo.LastObject < 128)
                        {
                            pinKind = "LikelyAsyncDependentPinned";
                            lastHandleInfo.LikelyAsyncHandleTable2 = lastHandleInfo.LikelyAsyncHandleTable1;
                            lastHandleInfo.LikelyAsyncHandleTable1 = lastHandleInfo.LastHandle;
                        }

                        // Remember our values for heuristics we use to determine if it is an async 
                        lastHandleInfo.LastHandle = data.HandleID;
                        lastHandleInfo.LastObject = data.ObjectID;
                    }

                    var objectInfo = gcHeapSimulators[process].GetObjectInfo(data.ObjectID);
                    if (objectInfo != null)
                    {
                        allocStack = objectInfo.AllocStack;
                        if ((allocStack != StackSourceCallStackIndex.Invalid) && (objectInfo.ClassFrame != StackSourceFrameIndex.Invalid))
                        {
                            if (512 <= objectInfo.Size)
                            {
                                var frameName = stackSource.GetFrameName(objectInfo.ClassFrame, false);

                                var size = 1024;
                                while (size < objectInfo.Size)
                                {
                                    size = size * 2;
                                }

                                frameName += " <= " + (size / 1024).ToString() + "K";
                                allocStack = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(frameName), allocStack);
                            }
                            else
                            {
                                allocStack = stackSource.Interner.CallStackIntern(objectInfo.ClassFrame, allocStack);
                            }
                        }
                    }

                    // If we did not get pinning information, see if it is a stack pin
                    if (pinStack == StackSourceCallStackIndex.Invalid)
                    {
                        const UInt64 allocQuantum = 0x10000 - 1;   // 64K, must be a power of 2.  

                        var threadStack = threadStacks[(int)process.ProcessIndex];
                        if (threadStack == null)
                        {
                            threadStacks[(int)process.ProcessIndex] = threadStack = new Dictionary<UInt64, TraceThread>();

                            foreach (var procThread in process.Threads)
                            {
                                // Round up to the next 64K boundary
                                var loc = (procThread.UserStackBase + allocQuantum) & ~allocQuantum;
                                // We assume thread stacks are .5 meg (8 * 64K)   Growing down.  
                                for (int i = 0; i < 8; i++)
                                {
                                    threadStack[loc] = procThread;
                                    loc -= (allocQuantum + 1);
                                }
                            }
                        }
                        UInt64 roundUp = (data.HandleID + allocQuantum) & ~allocQuantum;
                        TraceThread stackThread;
                        if (threadStack.TryGetValue(roundUp, out stackThread) && stackThread.StartTimeRelativeMSec <= data.TimeStampRelativeMSec && data.TimeStampRelativeMSec < stackThread.EndTimeRelativeMSec)
                        {
                            pinKind = "StackPinned";
                            pinStack = stackSource.GetCallStackForThread(stackThread);
                        }
                    }

                    /*****  OK we now have all the information we collected, create the sample.  *****/
                    sample.StackIndex = StackSourceCallStackIndex.Invalid;

                    // Choose the stack to use 
                    if (allocStack != StackSourceCallStackIndex.Invalid)
                    {
                        sample.StackIndex = allocStack;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Allocation Location"), sample.StackIndex);
                    }
                    else if (pinStack != StackSourceCallStackIndex.Invalid)
                    {
                        sample.StackIndex = pinStack;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Pinning Location"), sample.StackIndex);
                    }
                    else
                    {
                        var gcThread = data.Thread();
                        if (gcThread == null)
                        {
                            return;             // TODO WARN
                        }

                        sample.StackIndex = stackSource.GetCallStackForThread(gcThread);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("GC Location"), sample.StackIndex);
                    }

                    // Add GC Number
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("GC_NUM " + gcIndex), sample.StackIndex);

                    // Duration of the pin. 
                    var pinDuration = "UNKNOWN";
                    if (pinStartTimeRelativeMSec != 0)
                    {
                        var pinDurationMSec = data.TimeStampRelativeMSec - pinStartTimeRelativeMSec;
                        var roundedDuration = Math.Pow(10.0, Math.Ceiling(Math.Log10(pinDurationMSec)));
                        pinDuration = "<= " + roundedDuration.ToString("n");
                    }
                    var pinDurationInfo = "PINNED_FOR " + pinDuration + " msec";
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(pinDurationInfo), sample.StackIndex);

                    // Add the Pin Kind;
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(pinKind), sample.StackIndex);

                    // Add the type and size 
                    var typeName = data.TypeName;
                    if (data.ObjectSize > 0)
                    {
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Type " + typeName + " Size: 0x" + data.ObjectSize.ToString("x")), sample.StackIndex);
                    }

                    // Add the generation.
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Generation " + gcGen), sample.StackIndex);

                    // _sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Handle 0x" + data.HandleID.ToString("x") +  " Object 0x" + data.ObjectID.ToString("x")), _sample.StackIndex);

                    // We now have the stack, fill in the rest of the _sample and add it to the stack source.  
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = 1;
                    stackSource.AddSample(sample);
                };
                eventSource.Clr.GCPinObjectAtGCTime += objectAtGCTime;
                clrPrivate.GCPinObjectAtGCTime += objectAtGCTime;         // TODO FIX NOW REMOVE AFTER PRIVATE IS GONE

                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName == "Pinning")
            {
                var clrPrivate = new ClrPrivateTraceEventParser(eventSource);
                var liveHandles = new Dictionary<long, GCHandleInfo>();
                int maxLiveHandles = 0;
                double maxLiveHandleRelativeMSec = 0;

                Action<SetGCHandleTraceData> onSetHandle = delegate (SetGCHandleTraceData data)
                {
                    if (!(data.Kind == GCHandleKind.AsyncPinned || data.Kind == GCHandleKind.Pinned))
                    {
                        return;
                    }

                    GCHandleInfo info;
                    var handle = (long)data.HandleID;
                    if (!liveHandles.TryGetValue(handle, out info))
                    {
                        liveHandles[handle] = info = new GCHandleInfo();
                        if (liveHandles.Count > maxLiveHandles)
                        {
                            maxLiveHandles = liveHandles.Count;
                            maxLiveHandleRelativeMSec = data.TimeStampRelativeMSec;
                        }
                        info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;
                        info.ObjectAddress = data.ObjectID;

                        // TODO deal with nulling out. 
                        string nodeName = (data.Kind == GCHandleKind.Pinned) ? "SinglePinned" : "AsyncPinned";
                        StackSourceFrameIndex frameIndex = stackSource.Interner.FrameIntern(nodeName);
                        StackSourceCallStackIndex callStackIndex = stackSource.Interner.CallStackIntern(frameIndex, stackSource.GetCallStack(data.CallStackIndex(), data));

                        // Add the generation.
                        nodeName = "Generation " + data.Generation;
                        frameIndex = stackSource.Interner.FrameIntern(nodeName);
                        info.PinStack = stackSource.Interner.CallStackIntern(frameIndex, callStackIndex);
                    }
                };
                clrPrivate.GCSetGCHandle += onSetHandle;
                eventSource.Clr.GCSetGCHandle += onSetHandle;

                Action<DestroyGCHandleTraceData> onDestroyHandle = delegate (DestroyGCHandleTraceData data)
                {
                    GCHandleInfo info;
                    var handle = (long)data.HandleID;
                    if (liveHandles.TryGetValue(handle, out info))
                    {
                        LogGCHandleLifetime(stackSource, sample, info, data.TimeStampRelativeMSec, log);
                        liveHandles.Remove(handle);
                    }
                };
                clrPrivate.GCDestroyGCHandle += onDestroyHandle;
                eventSource.Clr.GCDestoryGCHandle += onDestroyHandle;

                eventSource.Process();
                // Pick up any handles that were never destroyed.  
                foreach (var info in liveHandles.Values)
                {
                    LogGCHandleLifetime(stackSource, sample, info, eventLog.SessionDuration.TotalMilliseconds, log);
                }

                stackSource.DoneAddingSamples();
                log.WriteLine("The maximum number of live pinning handles is {0} at {1:n3} Msec ", maxLiveHandles, maxLiveHandleRelativeMSec);
            }

            else if (streamName == "Heap Snapshot Pinning")
            {
                GCPinnedObjectAnalyzer pinnedObjectAnalyzer = new GCPinnedObjectAnalyzer(FilePath, eventLog, stackSource, sample, log);
                pinnedObjectAnalyzer.Execute(GCPinnedObjectViewType.PinnedHandles);
            }
            else if (streamName == "Heap Snapshot Pinned Object Allocation")
            {
                GCPinnedObjectAnalyzer pinnedObjectAnalyzer = new GCPinnedObjectAnalyzer(FilePath, eventLog, stackSource, sample, log);
                pinnedObjectAnalyzer.Execute(GCPinnedObjectViewType.PinnedObjectAllocations);
            }
            else if (streamName == "CCW Ref Count")
            {
                // TODO use the callback model.  We seem to have an issue getting the names however. 
                foreach (var data in events.ByEventType<CCWRefCountChangeTraceData>())
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                    var operation = data.Operation;
                    if (operation.StartsWith("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        sample.Metric = -1;
                    }

                    var ccwRefKindName = "CCW " + operation;
                    var ccwRefKindIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindIndex, stackIndex);

                    var ccwRefCountName = "CCW NewRefCnt " + data.NewRefCount.ToString();
                    var ccwRefCountIndex = stackSource.Interner.FrameIntern(ccwRefCountName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefCountIndex, stackIndex);

                    var ccwInstanceName = "CCW Instance 0x" + data.COMInterfacePointer.ToString("x");
                    var ccwInstanceIndex = stackSource.Interner.FrameIntern(ccwInstanceName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwInstanceIndex, stackIndex);

                    var ccwTypeName = "CCW Type " + data.NameSpace + "." + data.ClassName;
                    var ccwTypeIndex = stackSource.Interner.FrameIntern(ccwTypeName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwTypeIndex, stackIndex);

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                }
                foreach (var data in events.ByEventType<CCWRefCountChangeAnsiTraceData>())
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                    var operation = data.Operation;
                    if (operation.StartsWith("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        sample.Metric = -1;
                    }

                    var ccwRefKindName = "CCW " + operation;
                    var ccwRefKindIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindIndex, stackIndex);

                    var ccwRefCountName = "CCW NewRefCnt " + data.NewRefCount.ToString();
                    var ccwRefCountIndex = stackSource.Interner.FrameIntern(ccwRefCountName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefCountIndex, stackIndex);

                    var ccwInstanceName = "CCW Instance 0x" + data.COMInterfacePointer.ToString("x");
                    var ccwInstanceIndex = stackSource.Interner.FrameIntern(ccwInstanceName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwInstanceIndex, stackIndex);

                    var ccwTypeName = "CCW Type " + data.NameSpace + "." + data.ClassName;
                    var ccwTypeIndex = stackSource.Interner.FrameIntern(ccwTypeName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwTypeIndex, stackIndex);

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                }
            }
            else if (streamName == ".NET Native CCW Ref Count")
            {
                // TODO FIX NOW, investigate the missing events.  All we know is that incs and dec are not
                // consistant with the RefCount value that is in the events.
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show(GuiApp.MainWindow,
                        "Warning: the Interop CCW events on which this data is based seem to be incomplete.\r\n" +
                        "There seem to be missing instrumentation, which make the referenct counts unreliable\r\n"
                        , "Data May be Incorrect");
                });

                var objectToTypeMap = new Dictionary<long, UInt64>(1000);
                var typeToNameMap = new Dictionary<UInt64, string>(100);
                var interopTraceEventParser = new InteropTraceEventParser(eventSource);
                Action<double, long, int, int, StackSourceCallStackIndex> handleCWWInfoArgs = (double timestamp, long objectID, int refCount, int metric, StackSourceCallStackIndex stackIndex) =>
                {
                    sample.Metric = metric;
                    sample.TimeRelativeMSec = timestamp;

                    var ccwRefKindName = $"CCW {(metric >= 0 ? "AddRef" : "Release")}";
                    var ccwRefKindNameIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindNameIndex, stackIndex);

                    var objectId = "Object ID 0x" + objectID.ToString("x");
                    var objectIdIndex = stackSource.Interner.FrameIntern(objectId);
                    stackIndex = stackSource.Interner.CallStackIntern(objectIdIndex, stackIndex);

                    UInt64 typeId;
                    if (objectToTypeMap.TryGetValue(objectID, out typeId))
                    {
                        string objectType = "Object Type ";
                        string typeName;
                        if (typeToNameMap.TryGetValue(typeId, out typeName))
                        {
                            objectType += typeName;
                        }
                        else
                        {
                            objectType += "0x" + typeId;
                        }

                        var objectTypeIndex = stackSource.Interner.FrameIntern(objectType);
                        stackIndex = stackSource.Interner.CallStackIntern(objectTypeIndex, stackIndex);
                    }
                    var ccwRefCount = "CCW NewRefCnt " + refCount;
                    var ccwRefCountIndex = stackSource.Interner.FrameIntern(ccwRefCount.ToString());
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefCountIndex, stackIndex);

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                };

                TypeNameSymbolResolver typeNameSymbolResolver = new TypeNameSymbolResolver(FilePath, log);

                interopTraceEventParser.AddCallbackForEvent<TaskCCWCreationArgs>(null, args =>
                {
                    if (!objectToTypeMap.ContainsKey(args.objectID))
                    {
                        objectToTypeMap.Add(args.objectID, args.targetObjectIDType);
                    }

                    // Attempt to resolve the type name.
                    if (!typeToNameMap.ContainsKey(args.targetObjectIDType))
                    {
                        TraceLoadedModule module = args.Process().LoadedModules.GetModuleContainingAddress(args.targetObjectIDType, args.TimeStampRelativeMSec);
                        if (module != null)
                        {
                            string typeName = typeNameSymbolResolver.ResolveTypeName((int)(args.targetObjectIDType - module.ModuleFile.ImageBase), module.ModuleFile, TypeNameSymbolResolver.TypeNameOptions.StripModuleName);
                            if (typeName != null)
                            {
                                typeToNameMap.Add(args.targetObjectIDType, typeName);
                            }
                        }
                    }
                });
                #region TaskCCWQueryRuntimeClassNameArgs commented for a while. TODO: get type info from pdb
                //interopTraceEventParser.AddCallbackForEvents<TaskCCWQueryRuntimeClassNameArgs>(args =>
                //{
                //    sample.Metric = 0;
                //    sample.TimeRelativeMSec = args.TimeStampRelativeMSec;
                //    var stackIndex = stackSource.GetCallStack(args.CallStackIndex(), args);

                //    var ccwRefKindName = "CCW QueryRuntimeClassName " + args.runtimeClassName;
                //    var ccwRefKindNameIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                //    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindNameIndex, stackIndex);

                //    sample.StackIndex = stackIndex;
                //    stackSource.AddSample(sample);
                //});
                #endregion
                interopTraceEventParser.AddCallbackForEvents<TaskCCWRefCountIncArgs>(args =>
                    handleCWWInfoArgs(args.TimeStampRelativeMSec, args.objectID, args.refCount, 1, stackSource.GetCallStack(args.CallStackIndex(), args)));
                interopTraceEventParser.AddCallbackForEvents<TaskCCWRefCountDecArgs>(args =>
                    handleCWWInfoArgs(args.TimeStampRelativeMSec, args.objectID, args.refCount, -1, stackSource.GetCallStack(args.CallStackIndex(), args)));
                eventSource.Process();
            }
            else if (streamName == "Windows Handle Ref Count")
            {
                var allocationsStacks = new Dictionary<long, StackSourceCallStackIndex>(200);

                Action<string, UInt64, int, int, TraceEvent> onHandleEvent = delegate (string handleTypeName, UInt64 objectInstance, int handleInstance, int handleProcess, TraceEvent data)
                {
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.StackIndex = StackSourceCallStackIndex.Invalid;

                    sample.Metric = 1;
                    // Closes use the call stack of the allocation site if possible (since that is more helpful)  
                    if (data.Opcode == (TraceEventOpcode)33)       // CloseHandle
                    {
                        sample.Metric = -1;

                        long key = (((long)handleProcess) << 32) + handleInstance;
                        StackSourceCallStackIndex stackIndex;
                        if (allocationsStacks.TryGetValue(key, out stackIndex))
                        {
                            sample.StackIndex = stackIndex;
                        }
                        // TODO should we keep track of the ref count and remove the entry when it drops past zero?  
                    }

                    // If this not a close() (Or if we could not find a stack for the close() make up a call stack from the event.  
                    if (sample.StackIndex == StackSourceCallStackIndex.Invalid)
                    {
                        StackSourceCallStackIndex stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                        // We want all stacks to be int he process where the handle exists.  But this not always the case
                        // If that happened abandon the stack and make up a pseudo stack that lets you know that happened. 
                        if (handleProcess != data.ProcessID)
                        {
                            stackIndex = StackSourceCallStackIndex.Invalid;
                            TraceProcess process = eventLog.Processes.GetProcess(handleProcess, data.TimeStampRelativeMSec);
                            if (process != null)
                            {
                                stackIndex = stackSource.GetCallStackForProcess(process);
                            }

                            var markerIndex = stackSource.Interner.FrameIntern("Handle Allocated Out of Process");
                            stackIndex = stackSource.Interner.CallStackIntern(markerIndex, stackIndex);
                        }

                        var nameIndex = stackSource.Interner.FrameIntern(data.EventName);
                        stackIndex = stackSource.Interner.CallStackIntern(nameIndex, stackIndex);

                        var instanceName = "Handle Instance " + handleInstance.ToString("n0") + " (0x" + handleInstance.ToString("x") + ")";
                        var instanceIndex = stackSource.Interner.FrameIntern(instanceName);
                        stackIndex = stackSource.Interner.CallStackIntern(instanceIndex, stackIndex);

                        var handleTypeIndex = stackSource.Interner.FrameIntern("Handle Type " + handleTypeName);
                        stackIndex = stackSource.Interner.CallStackIntern(handleTypeIndex, stackIndex);

                        //var objectName = "Object Instance 0x" + objectInstance.ToString("x");
                        //var objectIndex = stackSource.Interner.FrameIntern(objectName);
                        //stackIndex = stackSource.Interner.CallStackIntern(objectIndex, stackIndex);

                        sample.StackIndex = stackIndex;

                        long key = (((long)handleProcess) << 32) + handleInstance;
                        allocationsStacks[key] = stackIndex;
                    }

                    stackSource.AddSample(sample);
                };

                eventSource.Kernel.AddCallbackForEvents<ObjectHandleTraceData>(data => onHandleEvent(data.ObjectTypeName, data.Object, data.Handle, data.ProcessID, data));
                eventSource.Kernel.AddCallbackForEvents<ObjectDuplicateHandleTraceData>(data => onHandleEvent(data.ObjectTypeName, data.Object, data.TargetHandle, data.TargetProcessID, data));
                eventSource.Process();
            }
            else if (streamName.StartsWith("Processor"))
            {
                eventSource.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
                {
                    StackSourceCallStackIndex stackIndex;
                    var callStackIdx = data.CallStackIndex();
                    if (callStackIdx == CallStackIndex.Invalid)
                    {
                        return;
                    }

                    stackIndex = stackSource.GetCallStack(callStackIdx, data);

                    var processorPriority = "Processor (" + data.ProcessorNumber + ") Priority (" + data.Priority + ")";
                    stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(processorPriority), stackIndex);

                    sample.StackIndex = stackIndex;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = 1;
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
            }
            else if (streamName.StartsWith("Any"))
            {
                ActivityComputer activityComputer = null;
                StartStopActivityComputer startStopComputer = null;
                bool isAnyTaskTree = (streamName == "Any TaskTree");
                bool isAnyWithTasks = (streamName == "Any Stacks (with Tasks)");
                bool isAnyWithStartStop = (streamName == "Any Stacks (with StartStop Activities)");          // These have the call stacks 
                bool isAnyStartStopTreeNoCallStack = (streamName == "Any StartStopTree");               // These have just the start-stop activities.  
                if (isAnyTaskTree || isAnyWithTasks || isAnyWithStartStop || isAnyStartStopTreeNoCallStack)
                {
                    activityComputer = new ActivityComputer(eventSource, GetSymbolReader(log));

                    // Log a pseudo-event that indicates when the activity dies
                    activityComputer.Stop += delegate (TraceActivity activity, TraceEvent data)
                    {
                        // TODO This is a clone of the logic below, factor it.  
                        TraceThread thread = data.Thread();
                        if (thread != null)
                        {
                            return;
                        }

                        StackSourceCallStackIndex stackIndex;
                        if (isAnyTaskTree)
                        {
                            // Compute the stack where frames using an activity Name as a frame name.
                            stackIndex = activityComputer.GetActivityStack(stackSource, activityComputer.GetCurrentActivity(thread));
                        }
                        else if (isAnyStartStopTreeNoCallStack)
                        {
                            stackIndex = startStopComputer.GetStartStopActivityStack(stackSource, startStopComputer.GetCurrentStartStopActivity(thread, data), thread.Process);
                        }
                        else
                        {
                            Func<TraceThread, StackSourceCallStackIndex> topFrames = null;
                            if (isAnyWithStartStop)
                            {
                                topFrames = delegate (TraceThread topThread) { return startStopComputer.GetCurrentStartStopActivityStack(stackSource, thread, topThread); };
                            }

                            // Use the call stack 
                            stackIndex = activityComputer.GetCallStack(stackSource, data, topFrames);
                        }

                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("ActivityStop " + activity.ToString()), stackIndex);
                        sample.StackIndex = stackIndex;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                        sample.Metric = 1;
                        stackSource.AddSample(sample);
                    };

                    if (isAnyWithStartStop || isAnyStartStopTreeNoCallStack)
                    {
                        startStopComputer = new StartStopActivityComputer(eventSource, activityComputer);
                    }
                }

                StackSourceFrameIndex blockingFrame = stackSource.Interner.FrameIntern("Event Kernel/Thread/BLOCKING CSwitch");
                StackSourceFrameIndex cswitchEventFrame = stackSource.Interner.FrameIntern("Event Windows Kernel/Thread/CSwitch");
                StackSourceFrameIndex readyThreadEventFrame = stackSource.Interner.FrameIntern("Event Windows Kernel/Dispatcher/ReadyThread");
                StackSourceFrameIndex sampledProfileFrame = stackSource.Interner.FrameIntern("Event Windows Kernel/PerfInfo/Sample");

                eventSource.AllEvents += delegate (TraceEvent data)
                {
                    // Get most of the stack (we support getting the normal call stack as well as the task stack.  
                    StackSourceCallStackIndex stackIndex;
                    if (activityComputer != null)
                    {
                        TraceThread thread = data.Thread();
                        if (thread == null)
                        {
                            return;
                        }

                        if (isAnyTaskTree)
                        {
                            // Compute the stack where frames using an activity Name as a frame name.
                            stackIndex = activityComputer.GetActivityStack(stackSource, activityComputer.GetCurrentActivity(thread));
                        }
                        else if (isAnyStartStopTreeNoCallStack)
                        {
                            stackIndex = startStopComputer.GetStartStopActivityStack(stackSource, startStopComputer.GetCurrentStartStopActivity(thread, data), thread.Process);
                        }
                        else
                        {
                            Func<TraceThread, StackSourceCallStackIndex> topFrames = null;
                            if (isAnyWithStartStop)
                            {
                                topFrames = delegate (TraceThread topThread) { return startStopComputer.GetCurrentStartStopActivityStack(stackSource, thread, topThread); };
                            }

                            // Use the call stack 
                            stackIndex = activityComputer.GetCallStack(stackSource, data, topFrames);
                        }
                    }
                    else
                    {
                        // Normal case, get the calls stack of frame names.  
                        var callStackIdx = data.CallStackIndex();
                        if (callStackIdx != CallStackIndex.Invalid)
                        {
                            stackIndex = stackSource.GetCallStack(callStackIdx, data);
                        }
                        else
                        {
                            stackIndex = StackSourceCallStackIndex.Invalid;
                        }
                    }

                    var asCSwitch = data as CSwitchTraceData;
                    if (asCSwitch != null)
                    {
                        if (activityComputer == null)  // Just a plain old any-stacks
                        {
                            var callStackIdx = asCSwitch.BlockingStack();
                            if (callStackIdx != CallStackIndex.Invalid)
                            {
                                StackSourceCallStackIndex blockingStackIndex = stackSource.GetCallStack(callStackIdx, data);
                                // Make an entry for the blocking stacks as well.  
                                blockingStackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData OldThreadState " + asCSwitch.OldThreadState), blockingStackIndex);
                                sample.StackIndex = stackSource.Interner.CallStackIntern(blockingFrame, blockingStackIndex);
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                sample.Metric = 1;
                                stackSource.AddSample(sample);
                            }
                        }

                        if (stackIndex != StackSourceCallStackIndex.Invalid)
                        {
                            stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData NewProcessName " + asCSwitch.NewProcessName), stackIndex);
                            stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData OldProcessName " + asCSwitch.OldProcessName), stackIndex);

                            stackIndex = stackSource.Interner.CallStackIntern(cswitchEventFrame, stackIndex);
                        }

                        goto ADD_SAMPLE;
                    }

                    if (stackIndex == StackSourceCallStackIndex.Invalid)
                    {
                        return;
                    }

                    var asSampledProfile = data as SampledProfileTraceData;
                    if (asSampledProfile != null)
                    {
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Priority " + asSampledProfile.Priority), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Processor " + asSampledProfile.ProcessorNumber), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(sampledProfileFrame, stackIndex);
                        goto ADD_SAMPLE;
                    }

                    var asReadyThread = data as DispatcherReadyThreadTraceData;
                    if (asReadyThread != null)
                    {
                        var awakenedName = "EventData Readied Thread " + asReadyThread.AwakenedThreadID +
                                           " Proc " + asReadyThread.AwakenedProcessID;
                        var awakenedIndex = stackSource.Interner.FrameIntern(awakenedName);
                        stackIndex = stackSource.Interner.CallStackIntern(awakenedIndex, stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(readyThreadEventFrame, stackIndex);
                        goto ADD_SAMPLE;
                    }

                    // TODO FIX NOW remove for debugging activity stuff.  
#if false
                    var activityId = data.ActivityID;
                    if (activityId != Guid.Empty && ActivityComputer.IsActivityPath(activityId))
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("ActivityPath " + ActivityComputer.ActivityPathString(activityId)), stackIndex);
#endif
                    var asObjectAllocated = data as ObjectAllocatedArgs;
                    if (asObjectAllocated != null)
                    {
                        var size = "EventData Size 0x" + asObjectAllocated.Size.ToString("x");
                        var sizeIndex = stackSource.Interner.FrameIntern(size);
                        stackIndex = stackSource.Interner.CallStackIntern(sizeIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asAllocTick = data as GCAllocationTickTraceData;
                    if (asAllocTick != null)
                    {
                        var frameIdx = stackSource.Interner.FrameIntern("EventData Kind " + asAllocTick.AllocationKind);
                        stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                        frameIdx = stackSource.Interner.FrameIntern("EventData Size " + asAllocTick.AllocationAmount64);
                        stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                        var typeName = asAllocTick.TypeName;
                        if (string.IsNullOrEmpty(typeName))
                        {
                            typeName = "TypeId 0x" + asAllocTick.TypeID;
                        }

                        frameIdx = stackSource.Interner.FrameIntern("EventData TypeName " + typeName);
                        stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asSampleObjectAllocated = data as GCSampledObjectAllocationTraceData;
                    if (asSampleObjectAllocated != null)
                    {
                        var size = "EventData Size 0x" + asSampleObjectAllocated.TotalSizeForTypeSample.ToString("x");
                        var sizeIndex = stackSource.Interner.FrameIntern(size);
                        stackIndex = stackSource.Interner.CallStackIntern(sizeIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asSetGCHandle = data as SetGCHandleTraceData;
                    if (asSetGCHandle != null)
                    {
                        var handleName = "EventData GCHandleKind " + asSetGCHandle.Kind.ToString();
                        var handleIndex = stackSource.Interner.FrameIntern(handleName);
                        stackIndex = stackSource.Interner.CallStackIntern(handleIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asPageAccess = data as MemoryPageAccessTraceData;
                    if (asPageAccess != null)
                    {
                        sample.Metric = 4;      // Convenience since these are 4K pages 

                        // EMit the kind, which may have a file name argument.  
                        var pageKind = asPageAccess.PageKind;
                        string fileName = asPageAccess.FileName;
                        if (fileName == null)
                        {
                            fileName = "";
                        }

                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(pageKind.ToString() + " " + fileName), stackIndex);

                        // If it is the range of a module, log that as well, as well as it bucket.  
                        var address = asPageAccess.VirtualAddress;
                        var process = data.Process();
                        if (process != null)
                        {
                            var module = process.LoadedModules.GetModuleContainingAddress(address, asPageAccess.TimeStampRelativeMSec);
                            if (module != null)
                            {
                                if (module.ModuleFile != null && module.ModuleFile.ImageSize != 0)
                                {
                                    // Create a node that indicates where in the file (in buckets) the access was from 
                                    double normalizeDistance = (address - module.ImageBase) / ((double)module.ModuleFile.ImageSize);
                                    if (0 <= normalizeDistance && normalizeDistance < 1)
                                    {
                                        const int numBuckets = 20;
                                        int bucket = (int)(normalizeDistance * numBuckets);
                                        int bucketSizeInPages = module.ModuleFile.ImageSize / (numBuckets * 4096);
                                        string bucketName = "Image Bucket " + bucket + " Size " + bucketSizeInPages + " Pages";
                                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(bucketName), stackIndex);
                                    }
                                }
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Image  " + module.ModuleFile.FilePath), stackIndex);
                            }
                        }
                        goto ADD_EVENT_FRAME;
                    }

                    var asPMCCounter = data as PMCCounterProfTraceData;
                    if (asPMCCounter != null)
                    {
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Processor " + asPMCCounter.ProcessorNumber), stackIndex);
                        var source = "EventData ProfileSourceID " + asPMCCounter.ProfileSource;
                        var sourceIndex = stackSource.Interner.FrameIntern(source);
                        stackIndex = stackSource.Interner.CallStackIntern(sourceIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asFileCreate = data as FileIOCreateTraceData;
                    if (asFileCreate != null)
                    {
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("CreateOptions: " + asFileCreate.CreateOptions), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("FileAttributes: " + asFileCreate.FileAttributes), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("ShareAccess: " + asFileCreate.ShareAccess), stackIndex);
                        // stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("CreateDispostion: " + asFileCreate.CreateDispostion), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("FileName: " + asFileCreate.FileName), stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    // Tack on additional info about the event. 
                    var fieldNames = data.PayloadNames;
                    for (int i = 0; i < fieldNames.Length; i++)
                    {
                        var fieldName = fieldNames[i];
                        if (0 <= fieldName.IndexOf("Name", StringComparison.OrdinalIgnoreCase) ||
                            fieldName == "OpenPath" || fieldName == "Url" || fieldName == "Uri" || fieldName == "ConnectionId" ||
                            fieldName == "ExceptionType" || 0 <= fieldName.IndexOf("Message", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = data.PayloadString(i);
                            var fieldNodeName = "EventData " + fieldName + " " + value;
                            var fieldNodeIndex = stackSource.Interner.FrameIntern(fieldNodeName);
                            stackIndex = stackSource.Interner.CallStackIntern(fieldNodeIndex, stackIndex);
                        }
                    }

                    ADD_EVENT_FRAME:
                    // Tack on event name 
                    var eventNodeName = "Event " + data.ProviderName + "/" + data.EventName;
                    stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(eventNodeName), stackIndex);
                    ADD_SAMPLE:
                    sample.StackIndex = stackIndex;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = 1;
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
            }
            else if (streamName == "Managed Load")
            {
                eventSource.Clr.LoaderModuleLoad += delegate (ModuleLoadUnloadTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var nodeName = "Load " + data.ModuleILPath;
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
            }
            else if (streamName == "Disk I/O")
            {
                var diskStartStack = new Dictionary<UInt64, StackSourceCallStackIndex>(50);
                // On a per-disk basis remember when the last Disk I/O completed.  
                var lastDiskEndMSec = new GrowableArray<double>(4);

                eventSource.Kernel.AddCallbackForEvents<DiskIOInitTraceData>(delegate (DiskIOInitTraceData data)
                {
                    diskStartStack[data.Irp] = stackSource.GetCallStack(data.CallStackIndex(), data);
                });

                eventSource.Kernel.AddCallbackForEvents<DiskIOTraceData>(delegate (DiskIOTraceData data)
                {
                    StackSourceCallStackIndex stackIdx;
                    if (diskStartStack.TryGetValue(data.Irp, out stackIdx))
                    {
                        diskStartStack.Remove(data.Irp);
                    }
                    else
                    {
                        stackIdx = StackSourceCallStackIndex.Invalid;
                    }

                    var diskNumber = data.DiskNumber;
                    if (diskNumber >= lastDiskEndMSec.Count)
                    {
                        lastDiskEndMSec.Count = diskNumber + 1;
                    }

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var filePath = data.FileName;
                    if (filePath.Length == 0)
                    {
                        filePath = "UNKNOWN";
                    }

                    var nodeName = "I/O Size 0x" + data.TransferSize.ToString("x");
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    stackIdx = stackSource.Interner.CallStackIntern(nodeIndex, stackIdx);

                    nodeName = string.Format("Disk {0} DiskNum({1}) {2} ({3})", data.OpcodeName, diskNumber,
                        GetFileName(filePath), GetDirectoryName(filePath));

                    // The time it took actually using the disk is the smaller of either
                    // The elapsed time (because there were no other entries in the disk queue)
                    // OR the time since the last I/O completed (since that is when this one will start using the disk.
                    var elapsedMSec = data.ElapsedTimeMSec;
                    double serviceTimeMSec = elapsedMSec;
                    double durationSinceLastIOMSec = data.TimeStampRelativeMSec - lastDiskEndMSec[diskNumber];
                    lastDiskEndMSec[diskNumber] = elapsedMSec;
                    if (durationSinceLastIOMSec < serviceTimeMSec)
                    {
                        serviceTimeMSec = durationSinceLastIOMSec;
                        // There is queuing delay, indicate this by adding a sample that represents the queueing delay. 

                        var queueStackIdx = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Time in Disk Queue " + diskNumber), stackIdx);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(nodeName), queueStackIdx);
                        sample.Metric = (float)(elapsedMSec - serviceTimeMSec);
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec - elapsedMSec;
                        stackSource.AddSample(sample);
                    }

                    stackIdx = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Service Time Disk " + diskNumber), stackIdx);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(nodeName), stackIdx);
                    sample.Metric = (float)serviceTimeMSec;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec - serviceTimeMSec;
                    stackSource.AddSample(sample);
                });
                eventSource.Process();
                m_extraTopStats = " Metric is MSec";
            }
            else if (streamName == "Server Request CPU")
            {
                ServerRequestScenarioConfiguration scenarioConfiguration = new ServerRequestScenarioConfiguration(eventLog);
                ComputingResourceStateMachine stateMachine = new ComputingResourceStateMachine(stackSource, scenarioConfiguration, ComputingResourceViewType.CPU);
                stateMachine.Execute();
            }
            else if (streamName == "Server Request Thread Time")
            {
                ServerRequestScenarioConfiguration scenarioConfiguration = new ServerRequestScenarioConfiguration(eventLog);
                ComputingResourceStateMachine stateMachine = new ComputingResourceStateMachine(stackSource, scenarioConfiguration, ComputingResourceViewType.ThreadTime);
                stateMachine.Execute();
            }
            else if (streamName == "Server Request Managed Allocation")
            {
                ServerRequestScenarioConfiguration scenarioConfiguration = new ServerRequestScenarioConfiguration(eventLog);
                ComputingResourceStateMachine stateMachine = new ComputingResourceStateMachine(stackSource, scenarioConfiguration, ComputingResourceViewType.Allocations);
                stateMachine.Execute();
            }
            else if (streamName == "Execution Tracing")
            {
                TraceLogEventSource source = eventLog.Events.GetSource();

                Action<TraceEvent> tracingCallback = delegate (TraceEvent data)
                {
                    string assemblyName = (string)data.PayloadByName("assembly");
                    string typeName = (string)data.PayloadByName("type");
                    string methodName = (string)data.PayloadByName("method");

                    string frameName = string.Format("{0}!{1}.{2}", assemblyName, typeName, methodName);

                    StackSourceCallStackIndex callStackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);
                    StackSourceFrameIndex nodeIndex = stackSource.Interner.FrameIntern(frameName);
                    callStackIndex = stackSource.Interner.CallStackIntern(nodeIndex, callStackIndex);

                    sample.Count = 1;
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.StackIndex = callStackIndex;

                    stackSource.AddSample(sample);
                };

                source.Dynamic.AddCallbackForProviderEvent("MethodCallLogger", "MethodEntry", tracingCallback);

                source.Process();
            }
            else if (streamName == "File I/O")
            {
                eventSource.Kernel.AddCallbackForEvents<FileIOReadWriteTraceData>(delegate (FileIOReadWriteTraceData data)
                {
                    sample.Metric = (float)data.IoSize;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    StackSourceCallStackIndex stackIdx = stackSource.GetCallStack(data.CallStackIndex(), data);

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var filePath = data.FileName;
                    if (filePath.Length == 0)
                    {
                        filePath = "UNKNOWN";
                    }

                    var nodeName = string.Format("File {0}: {1} ({2})", data.OpcodeName,
                        GetFileName(filePath), GetDirectoryName(filePath));
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    stackIdx = stackSource.Interner.CallStackIntern(nodeIndex, stackIdx);

                    sample.StackIndex = stackIdx;
                    stackSource.AddSample(sample);
                });
                eventSource.Process();
            }
            else if (streamName == "Image Load")
            {
                var loadedImages = new Dictionary<UInt64, StackSourceCallStackIndex>(100);
                Action<ImageLoadTraceData> imageLoadUnload = delegate (ImageLoadTraceData data)
                {
                    // TODO this is not really correct, it assumes process IDs < 64K and images bases don't use lower bits
                    // but it is true 
                    UInt64 imageKey = data.ImageBase + (UInt64)data.ProcessID;

                    sample.Metric = data.ImageSize;
                    if (data.Opcode == TraceEventOpcode.Stop)
                    {
                        sample.StackIndex = StackSourceCallStackIndex.Invalid;
                        StackSourceCallStackIndex allocIdx;
                        if (loadedImages.TryGetValue(imageKey, out allocIdx))
                        {
                            sample.StackIndex = allocIdx;
                        }

                        sample.Metric = -sample.Metric;
                    }
                    else
                    {
                        // Create a call stack that ends with 'Load <fileName> (<fileDirectory>)'
                        var fileName = data.FileName;
                        var nodeName = "Image Load " + GetFileName(fileName) + " (" + GetDirectoryName(fileName) + ")";
                        var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                        loadedImages[imageKey] = sample.StackIndex;
                    }
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    stackSource.AddSample(sample);
                };
                eventSource.Kernel.ImageLoad += imageLoadUnload;
                eventSource.Kernel.ImageUnload += imageLoadUnload;
                eventSource.Process();
            }
            else if (streamName == "Net Virtual Alloc")
            {
                var droppedEvents = 0;
                var memStates = new MemState[eventLog.Processes.Count];
                eventSource.Kernel.AddCallbackForEvents<VirtualAllocTraceData>(delegate (VirtualAllocTraceData data)
                {
                    bool isAlloc = false;
                    if ((data.Flags & (
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT |
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT |
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) != 0)
                    {
                        // Can't use data.Process() because some of the virtual allocs occur in the process that started the
                        // process and occur before the process start event, which is what Process() uses to find it. 
                        // TODO this code assumes that process launch is within 1 second and process IDs are not aggressively reused. 
                        var processWhereMemoryAllocated = data.Log().Processes.GetProcess(data.ProcessID, data.TimeStampRelativeMSec + 1000);
                        if (processWhereMemoryAllocated == null)
                        {
                            droppedEvents++;
                            return;
                        }

                        var processIndex = processWhereMemoryAllocated.ProcessIndex;
                        var memState = memStates[(int)processIndex];
                        if (memState == null)
                        {
                            memState = memStates[(int)processIndex] = new MemState();
                        }

                        // Commit and decommit not both on together.  
                        Debug.Assert((data.Flags &
                                      (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT)) !=
                                     (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT));

                        var stackIndex = StackSourceCallStackIndex.Invalid;
                        if ((data.Flags & VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT) != 0)
                        {
                            isAlloc = true;
                            // Some of the early allocations are actually by the process that starts this process.  Don't use their stacks 
                            // But do count them.  
                            var processIDAllocatingMemory = processWhereMemoryAllocated.ProcessID;  // This is not right, but it sets the condition properly below 
                            var thread = data.Thread();
                            if (thread != null)
                            {
                                processIDAllocatingMemory = thread.Process.ProcessID;
                            }

                            if (data.TimeStampRelativeMSec >= processWhereMemoryAllocated.StartTimeRelativeMsec && processIDAllocatingMemory == processWhereMemoryAllocated.ProcessID)
                            {
                                stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);
                            }
                            else
                            {
                                stackIndex = stackSource.GetCallStackForProcess(processWhereMemoryAllocated);
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Allocated In Parent Process"), stackIndex);
                            }
                        }
                        memState.Update(data.BaseAddr, data.Length, isAlloc, stackIndex,
                            delegate (long metric, StackSourceCallStackIndex allocStack)
                            {
                                Debug.Assert(allocStack != StackSourceCallStackIndex.Invalid);
                                Debug.Assert(metric != 0);                                                  // They should trim this already.  
                                sample.Metric = metric;
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                sample.StackIndex = allocStack;
                                stackSource.AddSample(sample);
                                // Debug.WriteLine("Sample Proc {0,12} Time {1,8:f3} Length 0x{2:x} Metric 0x{3:x} Stack {4,8} Cum {5,8}", process.Name, sample.TimeRelativeMSec, data.Length, (int) sample.Metric, (int)sample.StackIndex, memState.TotalMem);
                            });
                    }
                });
                eventSource.Process();
                if (droppedEvents != 0)
                {
                    log.WriteLine("WARNING: {0} events were dropped because their process could not be determined.", droppedEvents);
                }
            }
            else if (streamName == "Net Virtual Reserve")
            {
                // Mapped file (which includes image loads) logic. 
                var mappedImages = new Dictionary<UInt64, StackSourceCallStackIndex>(100);
                Action<MapFileTraceData> mapUnmapFile = delegate (MapFileTraceData data)
                {
                    sample.Metric = data.ViewSize;
                    // If it is a UnMapFile or MapFileDCStop event
                    if (data.Opcode == (TraceEventOpcode)38)
                    {
                        Debug.Assert(data.OpcodeName == "UnmapFile");
                        sample.StackIndex = StackSourceCallStackIndex.Invalid;
                        StackSourceCallStackIndex allocIdx;
                        if (mappedImages.TryGetValue(data.FileKey, out allocIdx))
                        {
                            sample.StackIndex = allocIdx;
                            mappedImages.Remove(data.FileKey);
                        }
                        sample.Metric = -sample.Metric;
                    }
                    else
                    {
                        Debug.Assert(data.OpcodeName == "MapFile" || data.OpcodeName == "MapFileDCStart");
                        // Create a call stack that ends with 'MapFile <fileName> (<fileDirectory>)'
                        var nodeName = "MapFile";
                        var fileName = data.FileName;
                        if (fileName.Length > 0)
                        {
                            nodeName = nodeName + " " + GetFileName(fileName) + " (" + GetDirectoryName(fileName) + ")";
                        }

                        var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                        mappedImages[data.FileKey] = sample.StackIndex;
                    }
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    stackSource.AddSample(sample);
                };
                eventSource.Kernel.FileIOMapFile += mapUnmapFile;
                eventSource.Kernel.FileIOUnmapFile += mapUnmapFile;
                eventSource.Kernel.FileIOMapFileDCStart += mapUnmapFile;

                // Virtual Alloc logic
                var droppedEvents = 0;
                var memStates = new MemState[eventLog.Processes.Count];
                var virtualReserverFrame = stackSource.Interner.FrameIntern("VirtualReserve");
                eventSource.Kernel.AddCallbackForEvents<VirtualAllocTraceData>(delegate (VirtualAllocTraceData data)
                {
                    bool isAlloc = false;
                    if ((data.Flags & (
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT |
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE |
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) != 0)
                    {
                        // Can't use data.Process() because some of the virtual allocs occur in the process that started the
                        // process and occur before the process start event, which is what Process() uses to find it. 
                        // TODO this code assumes that process launch is within 1 second and process IDs are not aggressively reused. 
                        var processWhereMemoryAllocated = data.Log().Processes.GetProcess(data.ProcessID, data.TimeStampRelativeMSec + 1000);
                        if (processWhereMemoryAllocated == null)
                        {
                            droppedEvents++;
                            return;
                        }

                        var processIndex = processWhereMemoryAllocated.ProcessIndex;
                        var memState = memStates[(int)processIndex];
                        if (memState == null)
                        {
                            memState = memStates[(int)processIndex] = new MemState();
                        }

                        // Commit and decommit not both on together.  
                        Debug.Assert((data.Flags &
                                      (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT)) !=
                                     (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT));
                        // Reserve and release not both on together.
                        Debug.Assert((data.Flags &
                                      (VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE | VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) !=
                                     (VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE | VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE));

                        // You allocate by committing or reserving.  We have already filtered out decommits which have no effect on reservation.  
                        // Thus the only memRelease is the only one that frees.  
                        var stackIndex = StackSourceCallStackIndex.Invalid;
                        if ((data.Flags & (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE)) != 0)
                        {
                            isAlloc = true;
                            // Some of the early allocations are actually by the process that starts this process.  Don't use their stacks 
                            // But do count them.  
                            var processIDAllocatingMemory = processWhereMemoryAllocated.ProcessID;  // This is not right, but it sets the condition properly below 
                            var thread = data.Thread();
                            if (thread != null)
                            {
                                processIDAllocatingMemory = thread.Process.ProcessID;
                            }

                            if (data.TimeStampRelativeMSec >= processWhereMemoryAllocated.StartTimeRelativeMsec && processIDAllocatingMemory == processWhereMemoryAllocated.ProcessID)
                            {
                                stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);
                            }
                            else
                            {
                                stackIndex = stackSource.GetCallStackForProcess(processWhereMemoryAllocated);
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Allocated In Parent Process"), stackIndex);
                            }
                            stackIndex = stackSource.Interner.CallStackIntern(virtualReserverFrame, stackIndex);
                        }
                        memState.Update(data.BaseAddr, data.Length, isAlloc, stackIndex,
                            delegate (long metric, StackSourceCallStackIndex allocStack)
                            {
                                Debug.Assert(allocStack != StackSourceCallStackIndex.Invalid);
                                Debug.Assert(metric != 0);                                                  // They should trim this already.  
                                sample.Metric = metric;
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                sample.StackIndex = allocStack;
                                stackSource.AddSample(sample);
                                // Debug.WriteLine("Sample Proc {0,12} Time {1,8:f3} Length 0x{2:x} Metric 0x{3:x} Stack {4,8} Cum {5,8}", process.Name, sample.TimeRelativeMSec, data.Length, (int) sample.Metric, (int)sample.StackIndex, memState.TotalMem);
                            });
                    }
                });
                eventSource.Process();
                if (droppedEvents != 0)
                {
                    log.WriteLine("WARNING: {0} events were dropped because their process could not be determined.", droppedEvents);
                }
            }
            else if (streamName == "Net OS Heap Alloc")
            {
                // We index by heap address and then within the heap we remember the allocation stack
                var heaps = new Dictionary<UInt64, Dictionary<UInt64, StackSourceSample>>();

                var heapParser = new HeapTraceProviderTraceEventParser(eventSource);
                Dictionary<UInt64, StackSourceSample> lastHeapAllocs = null;

                // These three variables are used in the local function GetAllocationType defined below.
                // and are used to look up type names associated with the native allocations.   
                var loadedModules = new Dictionary<TraceModuleFile, NativeSymbolModule>();
                var allocationTypeNames = new Dictionary<CallStackIndex, string>();
                var symReader = GetSymbolReader(log, SymbolReaderOptions.CacheOnly);

                UInt64 lastHeapHandle = 0;

                float peakMetric = 0;
                StackSourceSample peakSample = null;
                float cumMetric = 0;
                float sumCumMetric = 0;
                int cumCount = 0;

                heapParser.HeapTraceAlloc += delegate (HeapAllocTraceData data)
                {
                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);

                    var callStackIndex = data.CallStackIndex();
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = data.AllocSize;
                    sample.StackIndex = stackSource.GetCallStack(callStackIndex, data);

                    // Add the 'Alloc < XXX' pseudo node. 
                    var nodeIndex = stackSource.Interner.FrameIntern(GetAllocName((uint)data.AllocSize));
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);

                    // Add the 'Type ALLOCATION_TYPE' if available.  
                    string allocationType = GetAllocationType(callStackIndex);
                    if (allocationType != null)
                    {
                        nodeIndex = stackSource.Interner.FrameIntern("Type " + allocationType);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);
                    }

                    var addedSample = stackSource.AddSample(sample);
                    allocs[data.AllocAddress] = addedSample;

                    cumMetric += sample.Metric;
                    if (cumMetric > peakMetric)
                    {
                        peakMetric = cumMetric;
                        peakSample = addedSample;
                    }
                    sumCumMetric += cumMetric;
                    cumCount++;

                    /*****************************************************************************/
                    // Performs a stack crawl to match the best typename to this allocation. 
                    // Returns null if no typename was found.
                    // This updates loadedModules and allocationTypeNames. It reads symReader/eventLog.
                    string GetAllocationType(CallStackIndex csi)
                    {
                        if (!allocationTypeNames.TryGetValue(csi, out var typeName))
                        {
                            const int frameLimit = 25; // typically you need about 10 frames to get out of the OS functions 
                            // to get to a frame that has type information.   We'll search up this many frames
                            // before giving up on getting type information for the allocation.  

                            int frameCount = 0;
                            for (var current = csi; current != CallStackIndex.Invalid && frameCount < frameLimit; current = eventLog.CallStacks.Caller(current), frameCount++)
                            {
                                var module = eventLog.CodeAddresses.ModuleFile(eventLog.CallStacks.CodeAddressIndex(current));
                                if (module == null)
                                    continue;

                                if (!loadedModules.TryGetValue(module, out var symbolModule))
                                {
                                    loadedModules[module] = symbolModule =
                                        (module.PdbSignature != Guid.Empty
                                            ? symReader.FindSymbolFilePath(module.PdbName, module.PdbSignature, module.PdbAge, module.FilePath)
                                            : symReader.FindSymbolFilePathForModule(module.FilePath)) is string pdb
                                            ? symReader.OpenNativeSymbolFile(pdb)
                                            : null;
                                }

                                typeName = symbolModule?.GetTypeForHeapAllocationSite(
                                    (uint)(eventLog.CodeAddresses.Address(eventLog.CallStacks.CodeAddressIndex(current)) - module.ImageBase)
                                ) ?? typeName;
                            }
                            allocationTypeNames[csi] = typeName;
                        }
                        return typeName;
                    }
                };

                heapParser.HeapTraceFree += delegate (HeapFreeTraceData data)
                {
                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                    {
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);
                    }

                    StackSourceSample alloc;
                    if (allocs.TryGetValue(data.FreeAddress, out alloc))
                    {
                        cumMetric -= alloc.Metric;
                        sumCumMetric += cumMetric;
                        cumCount++;

                        allocs.Remove(data.FreeAddress);

                        Debug.Assert(alloc.Metric >= 0);
                        sample.Metric = -alloc.Metric;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                        sample.StackIndex = alloc.StackIndex;
                        stackSource.AddSample(sample);
                    }
                };

                heapParser.HeapTraceReAlloc += delegate (HeapReallocTraceData data)
                {
                    // Reallocs that actually move stuff will cause a Alloc and delete event
                    // so there is nothing to do for those events.  But when the address is
                    // the same we need to resize 
                    if (data.OldAllocAddress != data.NewAllocAddress)
                    {
                        return;
                    }

                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                    {
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);
                    }

                    // This is a clone of the Free code 
                    StackSourceSample alloc;
                    if (allocs.TryGetValue(data.OldAllocAddress, out alloc))
                    {
                        cumMetric -= alloc.Metric;
                        sumCumMetric += cumMetric;
                        cumCount++;

                        allocs.Remove(data.OldAllocAddress);

                        Debug.Assert(alloc.Metric == data.OldAllocSize);
                        sample.Metric = -alloc.Metric;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                        sample.StackIndex = alloc.StackIndex;
                        stackSource.AddSample(sample);
                    }

                    // This is a clone of the Alloc code (sigh don't clone code)
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = data.NewAllocSize;
                    var nodeIndex = stackSource.Interner.FrameIntern(GetAllocName((uint)data.NewAllocSize));
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    var addedSample = stackSource.AddSample(sample);
                    allocs[data.NewAllocAddress] = addedSample;

                    cumMetric += sample.Metric;
                    if (cumMetric > peakMetric)
                    {
                        peakMetric = cumMetric;
                        peakSample = addedSample;
                    }
                    sumCumMetric += cumMetric;
                    cumCount++;
                };

                heapParser.HeapTraceDestroy += delegate (HeapTraceData data)
                {
                    // Heap is dieing, kill all objects in it.   
                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                    {
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);
                    }

                    foreach (StackSourceSample alloc in allocs.Values)
                    {
                        // TODO this is a clone of the free code.  
                        cumMetric -= alloc.Metric;
                        sumCumMetric += cumMetric;
                        cumCount++;

                        Debug.Assert(alloc.Metric >= 0);
                        sample.Metric = -alloc.Metric;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                        sample.StackIndex = alloc.StackIndex;
                        stackSource.AddSample(sample);
                    }
                };
                eventSource.Process();

                var aveCumMetric = sumCumMetric / cumCount;
                log.WriteLine("Peak Heap Size: {0:n3} MB   Average Heap size: {1:n3} MB", peakMetric / 1000000.0F, aveCumMetric / 1000000.0F);
                if (peakSample != null)
                {
                    log.WriteLine("Peak happens at {0:n3} Msec into the trace.", peakSample.TimeRelativeMSec);
                }

                log.WriteLine("Trimming alloc-free pairs < 3 msec apart: Before we have {0:n1}K events now {1:n1}K events",
                    cumCount / 1000.0, stackSource.SampleIndexLimit / 1000.0);
                return stackSource;
            }
            else if (streamName == "Server GC")
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(eventSource);
                Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.AddCallbackOnProcessStart(eventSource, proc =>
                {
                    Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.SetSampleIntervalMSec(proc, (float)eventLog.SampleProfileInterval.TotalMilliseconds);
                    Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.SetMutableTraceEventStackSource(proc, stackSource);
                });
                eventSource.Process();
                return stackSource;
            }
            else if(streamName == "Anti-Malware Real-Time Scan")
            {
                RealtimeAntimalwareComputer computer = new RealtimeAntimalwareComputer(eventSource, stackSource);
                computer.Execute();

                return stackSource;
            }
            else
            {
                throw new Exception("Unknown stream " + streamName);
            }

            log.WriteLine("Produced {0:n3}K events", stackSource.SampleIndexLimit / 1000.0);
            stackSource.DoneAddingSamples();
            return stackSource;
        }

        #region private
        private static StackSource GetProcessFileRegistryStackSource(TraceLogEventSource eventSource, TextWriter log)
        {
            TraceLog traceLog = eventSource.TraceLog;

            // This maps a process Index to the stack that represents that process.  
            StackSourceCallStackIndex[] processStackCache = new StackSourceCallStackIndex[traceLog.Processes.Count];
            for (int i = 0; i < processStackCache.Length; i++)
            {
                processStackCache[i] = StackSourceCallStackIndex.Invalid;
            }

            var stackSource = new MutableTraceEventStackSource(eventSource.TraceLog);

            StackSourceSample sample = new StackSourceSample(stackSource);

            var fileParser = new MicrosoftWindowsKernelFileTraceEventParser(eventSource);

            fileParser.Create += delegate (FileIOCreateTraceData data)
            {
                StackSourceCallStackIndex stackIdx = GetStackForProcess(data.Process(), traceLog, stackSource, processStackCache);
                stackIdx = stackSource.GetCallStack(data.CallStackIndex(), stackIdx);
                string imageFrameString = string.Format("FileOpenOrCreate {0}", data.FileName);
                StackSourceFrameIndex imageFrameIdx = stackSource.Interner.FrameIntern(imageFrameString);
                stackIdx = stackSource.Interner.CallStackIntern(imageFrameIdx, stackIdx);

                sample.Count = 1;
                sample.Metric = 1;
                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                sample.StackIndex = stackIdx;
                stackSource.AddSample(sample);
            };

            eventSource.Kernel.AddCallbackForEvents(delegate (ImageLoadTraceData data)
            {
                StackSourceCallStackIndex stackIdx = GetStackForProcess(data.Process(), traceLog, stackSource, processStackCache);
                stackIdx = stackSource.GetCallStack(data.CallStackIndex(), stackIdx);
                string fileCreateFrameString = string.Format("ImageLoad Base 0x{0:x} Size 0x{1:x} Name {2}", data.ImageBase, data.ImageSize, data.FileName);
                StackSourceFrameIndex fileCreateFrameIdx = stackSource.Interner.FrameIntern(fileCreateFrameString);
                stackIdx = stackSource.Interner.CallStackIntern(fileCreateFrameIdx, stackIdx);

                sample.Count = 1;
                sample.Metric = 1;
                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                sample.StackIndex = stackIdx;
                stackSource.AddSample(sample);
            });

            eventSource.Process();
            stackSource.DoneAddingSamples();
            return stackSource;
        }
        private static StackSourceCallStackIndex GetStackForProcess(TraceProcess process, TraceLog traceLog, MutableTraceEventStackSource stackSource, StackSourceCallStackIndex[] processStackCache)
        {
            if (process == null)
            {
                return StackSourceCallStackIndex.Invalid;
            }

            var ret = processStackCache[(int)process.ProcessIndex];
            if (ret == StackSourceCallStackIndex.Invalid)
            {
                StackSourceCallStackIndex parentStack = StackSourceCallStackIndex.Invalid;
                parentStack = GetStackForProcess(process.Parent, traceLog, stackSource, processStackCache);

                string parent = "";
                if (parentStack == StackSourceCallStackIndex.Invalid)
                {
                    parent += ",Parent=" + process.ParentID;
                }

                string command = process.CommandLine;
                if (string.IsNullOrWhiteSpace(command))
                {
                    command = process.ImageFileName;
                }

                string processFrameString = string.Format("Process({0}{1}): {2}", process.ProcessID, parent, command);

                StackSourceFrameIndex processFrameIdx = stackSource.Interner.FrameIntern(processFrameString);
                ret = stackSource.Interner.CallStackIntern(processFrameIdx, parentStack);
            }
            return ret;
        }

        private static string GetDirectoryName(string filePath)
        {
            // We need long (over 260) file name support so we do this by hand.  
            var lastSlash = filePath.LastIndexOf('\\');
            if (lastSlash < 0)
            {
                return "";
            }

            return filePath.Substring(0, lastSlash + 1);
        }

        private static string GetFileName(string filePath)
        {
            // We need long (over 260) file name support so we do this by hand.  
            var lastSlash = filePath.LastIndexOf('\\');
            if (lastSlash < 0)
            {
                return filePath;
            }

            return filePath.Substring(lastSlash + 1);
        }

        /// <summary>
        /// Implements a simple one-element cache for find the heap to look in.  
        /// </summary>
        private static Dictionary<UInt64, StackSourceSample> GetHeap(UInt64 heapHandle, Dictionary<UInt64, Dictionary<UInt64, StackSourceSample>> heaps, ref Dictionary<UInt64, StackSourceSample> lastHeapAllocs, ref UInt64 lastHeapHandle)
        {
            Dictionary<UInt64, StackSourceSample> ret;

            if (!heaps.TryGetValue(heapHandle, out ret))
            {
                ret = new Dictionary<UInt64, StackSourceSample>();
                heaps.Add(heapHandle, ret);
            }
            lastHeapHandle = heapHandle;
            lastHeapAllocs = ret;
            return ret;
        }

        private static void LogGCHandleLifetime(MutableTraceEventStackSource stackSource,
            StackSourceSample sample, GCHandleInfo info, double timeRelativeMSec, TextWriter log)
        {
            sample.Metric = (float)(timeRelativeMSec - info.PinStartTimeRelativeMSec);
            if (sample.Metric < 0)
            {
                log.WriteLine("Error got a negative time at {0:n3} started {1:n3}.  Dropping", timeRelativeMSec, info.PinStartTimeRelativeMSec);
                return;
            }

            var stackIndex = info.PinStack;
            var roundToLog = Math.Pow(10.0, Math.Ceiling(Math.Log10(sample.Metric)));
            var nodeName = "LIVE_FOR <= " + roundToLog + " msec";
            var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
            stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);

            nodeName = "OBJECT_INSTANCEID = " + info.ObjectAddress;
            nodeIndex = stackSource.Interner.FrameIntern(nodeName);
            stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);

            sample.TimeRelativeMSec = info.PinStartTimeRelativeMSec;
            sample.StackIndex = stackIndex;
            stackSource.AddSample(sample);
        }

        private class PerThreadGCHandleInfo
        {
            public UInt64 LastHandle;
            public UInt64 LastObject;
            public UInt64 LikelyAsyncHandleTable1;
            public UInt64 LikelyAsyncHandleTable2;
        }

        private class GCHandleInfo
        {
            public double PinStartTimeRelativeMSec;
            public UInt64 ObjectAddress;
            public StackSourceCallStackIndex PinStack = StackSourceCallStackIndex.Invalid;
            public bool IsAsync;
            public byte GCGen;
        }

        public override List<IProcess> GetProcesses(TextWriter log)
        {
            var processes = new List<IProcess>();

            var eventLog = GetTraceLog(log);
            foreach (var process in eventLog.Processes)
            {
                var iprocess = new IProcessForStackSource(process.Name);
                iprocess.StartTime = process.StartTime;
                iprocess.EndTime = process.EndTime;
                iprocess.CPUTimeMSec = process.CPUMSec;
                iprocess.ParentID = process.ParentID;
                iprocess.CommandLine = process.CommandLine;
                iprocess.ProcessID = process.ProcessID;
                processes.Add(iprocess);
            }
            processes.Sort();
            return processes;
        }


        /// <summary>
        /// Class keeps track of the memory state given virtual allocs.  Basically you have to model what memory is allocated 
        /// </summary>
        private class MemState
        {
            public MemState()
            {
                m_searchTable.Add(new Region(0, Region.FreeStackIndex, null));  // Memory starts out completely free.  
                m_numRegions = 1;
            }
            [Conditional("DEBUG")]
            private void ClassInvarient()
            {
                Debug.Assert(0 < m_searchTable.Count);
                var prev = m_searchTable[0];
                Debug.Assert(prev.MemAddr == 0);
                var cur = prev.Next;
                var regionCount = 1;        // Total number of regions in my linked list
                var curIdx = 1;             // Index in my sorted m_searchTable
                while (cur != null)
                {
                    // Update the curIdx.   Note that you can have multiple entries pointing to the same location (this is how we delete regions
                    // without having to shuffle the table.
                    while (curIdx < m_searchTable.Count && m_searchTable[curIdx] == cur)
                    {
                        curIdx++;
                    }

                    Debug.Assert(m_searchTable.Count <= curIdx || cur.MemAddr < m_searchTable[curIdx].MemAddr);

                    Debug.Assert(prev.MemAddr < cur.MemAddr);     // strictly increasing
                    Debug.Assert(!(cur.Next == null && cur.AllocStack != Region.FreeStackIndex && cur.MemAddr != ulong.MaxValue));
                    prev = cur;
                    cur = cur.Next;
                    regionCount++;
                }
                Debug.Assert(regionCount == m_numRegions);          // m_numRegions is accurate.  
                Debug.Assert(curIdx == m_searchTable.Count);        // One entries in the table are in the list.  
            }
#if DEBUG
            private int Count
            {
                get
                {
                    var cur = m_searchTable[0];
                    int cnt = 0;
                    while (cur != null)
                    {
                        cur = cur.Next;
                        cnt++;
                    }
                    return cnt;
                }
            }
#endif
#if DEBUG
            /// <summary>
            /// This routine is only used in asserts.   It represents the total amount of net memory that has been
            /// committed by all the VirtualAllocs/Frees that have occurred so far.  
            /// </summary>
            public long TotalMem
            {
                get
                {
                    long ret = 0;
                    var cur = m_searchTable[0];
                    while (cur != null)
                    {
                        if (!cur.IsFree)
                        {
                            ret += (long)(cur.Next.MemAddr - cur.MemAddr);
                        }

                        cur = cur.Next;
                    }
                    return ret;
                }
            }
#endif

            /// <summary>
            /// updates the memory state of [startAddr, startAddr+length) to be either allocated or free (based on 'isAlloc').  
            /// It returns the amount of memory delta (positive for allocation, negative for free).
            /// 
            /// What makes this a pain is VirtuaAlloc regions can overlap (you can 'commit' the same region multiple times, or
            /// free just a region within an allocation etc).   
            /// 
            /// Thus you have to keep track of exactly what is allocated (we keep a sorted list of regions), and do set operations
            /// on these regions.   This is what makes it non-trivial.  
            /// 
            /// if 'isAlloc' is true, then allocStack should be the stack at that allocation.  
            /// 
            /// 'callback' is called with two parameters (the net memory change (will be negative for frees), as well as the call
            /// stack for the ALLOCATION (even in the case of a free, it is the allocation stack that is logged).   
            /// 
            /// If an allocation overlaps with an existing allocation, only the NET allocation is indicated (the existing allocated
            /// region is subtracted out.   This means is is the 'last' allocation that gets 'charged' for a region.
            /// 
            /// The main point, however is that there is no double-counting and get 'perfect' matching of allocs and frees. 
            /// 
            /// There may be more than one callback issued if the given input region covers several previously allocated regions
            /// and thus need to be 'split up'.  In the case of a free, several callbacks could be issued because different 
            /// allocation call stacks were being freed in a single call.  
            /// </summary>
            internal void Update(UInt64 startAddr, long length, bool isAlloc, StackSourceCallStackIndex allocStack,
                Action<long, StackSourceCallStackIndex> callback)
            {
                Debug.Assert(startAddr != 0);                   // No on can allocate this virtual address.
                if (startAddr == 0)
                {
                    return;
                }

                UInt64 endAddr = startAddr + (UInt64)length;  // end of range
                if (endAddr == 0)                               // It is possible to wrap around (if you allocate the last region of memory. 
                {
                    endAddr = ulong.MaxValue;                   // Avoid this case by adjust it down a bit.  
                }

                Debug.Assert(endAddr > startAddr);
                if (!isAlloc)
                {
                    allocStack = Region.FreeStackIndex;
                }

                m_totalUpdates++;
#if DEBUG
                long memoryBeforeUpdate = TotalMem;
                long callBackNet = 0;               // How much we said the net allocation was for all the callbacks we make.  
#endif

                Debug.Assert(allocStack != StackSourceCallStackIndex.Invalid);
                // From time to time, update the search table to be 'perfect' if we see that chain length is too high.  
                if (m_totalUpdates > m_searchTable.Count && m_totalChainTraverals > MaxChainLength * m_totalUpdates)
                {
                    Debug.WriteLine(string.Format("Redoing Search table.  Num Regions {0} Table Size {1}  numUpdates {2} Average Chain Leng {3}",
                        m_numRegions, m_searchTable.Count, m_totalUpdates, m_totalChainTraverals / m_totalUpdates));
                    ExpandSearchTable();
                    m_totalUpdates = 0;
                    m_totalChainTraverals = 0;
                }

                int searchTableIdx;             // Points at prev or before.  
                m_searchTable.BinarySearch(startAddr - 1, out searchTableIdx, delegate (UInt64 x, Region y) { return x.CompareTo(y.MemAddr); });
                Debug.Assert(0 <= searchTableIdx);          // Can't get -1 because 0 is the smallest number 
                Region prev = m_searchTable[searchTableIdx];

                Region cur = prev.Next;                         // current node
                Debug.Assert(prev.MemAddr <= startAddr);

                Debug.WriteLine(string.Format("Addr {0:x} idx {1} prev {2:x}", startAddr, searchTableIdx, prev.MemAddr));
                for (int chainLength = 0; ; chainLength++)      // the amount of searching I need to do after binary search 
                {
                    m_totalChainTraverals++;

                    // If we fall off the end, 'clone' split the last region into one that exactly overlaps the new region.  
                    if (cur == null)
                    {
                        prev.Next = cur = new Region(endAddr, prev.AllocStack, null);
                        m_numRegions++;
                        if (chainLength > MaxChainLength)
                        {
                            m_searchTable.Add(cur);
                        }
                    }

                    // Does the new region start after (or at) prev and strictly before than cur? (that is, does the region overlap with prev?)
                    if (startAddr < cur.MemAddr)
                    {
                        var prevAllocStack = prev.AllocStack;       // Remember this since we clobber it.  

                        // Can I reuse the node (it starts at exactly the right place, or it is the same stack 
                        // (which I can coalesce))
                        if (startAddr == prev.MemAddr || prevAllocStack == allocStack)
                        {
                            prev.AllocStack = allocStack;
                        }
                        else
                        {
                            prev.Next = new Region(startAddr, allocStack, cur);
                            m_numRegions++;
                            prev = prev.Next;
                        }

                        // Try to break up long chains in the search table.   
                        if (chainLength > MaxChainLength)
                        {
                            Debug.Assert(searchTableIdx < m_searchTable.Count);
                            if (searchTableIdx + 1 == m_searchTable.Count)
                            {
                                m_searchTable.Add(prev);
                            }
                            else
                            {
                                Debug.Assert(m_searchTable[searchTableIdx].MemAddr <= prev.MemAddr);
                                // Make sure we remain sorted.   Note that we can exceed the next slot in the table because
                                // the region we are inserting 'covers' many table entries.   
                                if (m_searchTable.Count <= searchTableIdx + 2 || prev.MemAddr < m_searchTable[searchTableIdx + 2].MemAddr)
                                {
                                    m_searchTable[searchTableIdx + 1] = prev;
                                }
                            }
                            searchTableIdx++;
                            chainLength = 0;
                        }

                        // net is the amount we are freeing or allocating for JUST THIS FIRST overlapped region (prev to cur)
                        // We start out assuming that the new region is bigger than the current region, so the net is the full current region.  
                        long net = (long)(cur.MemAddr - startAddr);

                        // Does the new region fit completely between prev and cur?  
                        bool overlapEnded = (endAddr <= cur.MemAddr);
                        if (overlapEnded)
                        {
                            net = (long)(endAddr - startAddr);
                            // If it does not end exactly, we need to end our chunk and resume the previous region.  
                            if (endAddr != cur.MemAddr && prevAllocStack != allocStack)
                            {
                                prev.Next = new Region(endAddr, prevAllocStack, cur);
                                m_numRegions++;
                            }
                        }
                        Debug.Assert(net >= 0);

                        // Log the delta to the callback.  
                        StackSourceCallStackIndex stackToLog;
                        if (allocStack != Region.FreeStackIndex)        // Is the update an allocation.  
                        {
                            if (prevAllocStack != Region.FreeStackIndex)
                            {
                                net = 0;                                // committing a committed region, do nothing
                            }

                            stackToLog = allocStack;
                        }
                        else    // The update is a free.  
                        {
                            if (prevAllocStack == Region.FreeStackIndex)
                            {
                                net = 0;                                // freeing a freed region, do nothing  
                            }

                            net = -net;                                 // frees have negative weight. 
                            stackToLog = prevAllocStack;                // We attribute the free to the allocation call stack  
                        }
                        ClassInvarient();

                        if (net != 0)                                   // Make callbacks to user code if there is any change.  
                        {
#if DEBUG
                            callBackNet += net;
#endif
                            callback(net, stackToLog);                  // issue the callback
                        }

                        if (overlapEnded || endAddr == 0)               // Are we done?  (endAddr == 0 is for the case where the region wraps around).  
                        {
#if DEBUG
                            Debug.Assert(memoryBeforeUpdate + callBackNet == TotalMem);
                            Debug.WriteLine(string.Format("EXITING Num Regions {0} Table Size {1}  numUpdates {2} Average Chain Len {3}",
                                m_numRegions, m_searchTable.Count, m_totalUpdates, m_totalChainTraverals * 1.0 / m_totalUpdates));
#endif
                            // Debug.Write("**** Exit State\r\n" + this.ToString());
                            return;
                        }

                        startAddr = cur.MemAddr;       // Modify the region so that it no longer includes the overlap with 'prev'  
                    }

                    // we may be able to coalesce (probably free) nodes.  
                    if (prev.AllocStack == cur.AllocStack)
                    {
                        prev.Next = cur.Next;       // Remove cur (prev does not move)
                        --m_numRegions;

                        // Make sure there are no entries in the search table that point at the entry to be deleted.   
                        var idx = searchTableIdx;
                        do
                        {
                            Debug.Assert(m_searchTable[idx].MemAddr <= cur.MemAddr);
                            if (cur == m_searchTable[idx])
                            {
                                // Assert that we stay sorted.  
                                Debug.Assert(idx == 0 || m_searchTable[idx - 1].MemAddr <= prev.MemAddr);
                                Debug.Assert(idx + 1 == m_searchTable.Count || prev.MemAddr <= m_searchTable[idx + 1].MemAddr);
                                m_searchTable[idx] = prev;
                            }
                            idx++;
                        } while (idx < m_searchTable.Count && m_searchTable[idx].MemAddr <= cur.MemAddr);

                        ClassInvarient();
                    }
                    else
                    {
                        prev = cur;                 // prev advances to cur 
                    }

                    cur = cur.Next;
                }
            }

            /// <summary>
            /// Allocate a new search table that has all the regions in it with not chaining necessary.   
            /// </summary>
            private void ExpandSearchTable()
            {
                Region ptr = m_searchTable[0];
                m_searchTable = new GrowableArray<Region>(m_numRegions + MaxChainLength);   // Add a bit more to grow on the end if necessary.  
                while (ptr != null)
                {
                    m_searchTable.Add(ptr);
                    ptr = ptr.Next;
                }
                Debug.Assert(m_searchTable.Count == m_numRegions);
            }

            private const int MaxChainLength = 8;           // We don't want chain lengths bigger than this.  
            // The state of memory is represented as a (sorted) linked list of addresses (with a stack), 
            // Some of the regions are free (marked by FreeStackIndex)  They only have a start address so by 
            // construction they can't overlap.  

            private class Region
            {
                // The special value that represents a free region.  
                public const StackSourceCallStackIndex FreeStackIndex = (StackSourceCallStackIndex)(-2);
                /// <summary>
                /// Create an allocation region starting at 'startAddr' allocated at 'allocStack'
                /// </summary>
                public Region(UInt64 memAddr, StackSourceCallStackIndex allocStack, Region next) { MemAddr = memAddr; AllocStack = allocStack; Next = next; }
                public bool IsFree { get { return AllocStack == FreeStackIndex; } }

                public UInt64 MemAddr;
                public StackSourceCallStackIndex AllocStack;
                public Region Next;
            };
#if DEBUG
            public override string ToString()
            {
                var sb = new StringBuilder();
                var cur = m_searchTable[0];
                while (cur != null)
                {
                    sb.Append("[").Append(cur.MemAddr.ToString("X")).Append(" stack=").Append(cur.AllocStack).Append("]").AppendLine();
                    cur = cur.Next;
                }
                return sb.ToString();
            }
#endif
            /// <summary>
            /// The basic data structure here is a linked list where each element is ALSO in this GrowableArray of
            /// entry points into that list.   This array of entry points is SORTED, so we can do binary search to 
            /// find a particular entry in log(N) time.   However we want to support fast insertion (and I am too
            /// lazy to implement a self-balancing tree) so when we add entries we add them to the linked list but
            /// not necessarily to this binary search table.   From time to time we will 'fixup' this table to 
            /// be perfect again.   
            /// </summary>
            private GrowableArray<Region> m_searchTable;        // always non-empty, first entry is linked list to all entries.  

            // Keep track of enough to compute the average chain length on lookups.   
            private int m_totalChainTraverals;                  // links we have to traverse from the search table to get to the entry we want.
            private int m_totalUpdates;                         // Number of lookups we did.  (We reset after every table expansion).   
            private int m_numRegions;                           // total number of entries in our linked list (may be larger than the search table) 
        }


        private static string[] AllocNames = InitAllocNames();
        private static string[] InitAllocNames()
        {
            // Cache the names, so we don't create them on every event.  
            string[] ret = new string[16];
            int size = 1;
            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = "Alloc < " + size;
                size *= 2;
            }
            return ret;
        }
        private static string GetAllocName(uint metric)
        {
            string allocName;
            int log2Metric = 0;
            while (metric > 0)
            {
                metric >>= 1;
                log2Metric++;
            }
            if (log2Metric < AllocNames.Length)
            {
                allocName = AllocNames[log2Metric];
            }
            else
            {
                allocName = "Alloc >= 32768";
            }

            return allocName;
        }
        #endregion

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsEtwStackWindow(stackWindow, stackSourceName.StartsWith("CPU"));

            if (stackSourceName == "Processes / Files / Registry")
            {
                var defaultFold = @"^FileOpenOrCreate*:\Windows\Sys;^ImageLoad*:\Windows\Sys;^Process*conhost";
                stackWindow.FoldRegExTextBox.Items.Add(defaultFold);
                stackWindow.FoldRegExTextBox.Text = defaultFold;

                stackWindow.CallTreeTab.IsSelected = true;      // start with the call tree view
                return;
            }

            if (stackSourceName.Contains("(with Tasks)") || stackSourceName.Contains("(with StartStop Activities)"))
            {
                var taskFoldPatBase = "ntoskrnl!%ServiceCopyEnd;System.Runtime.CompilerServices.Async%MethodBuilder";
                var taskFoldPat = taskFoldPatBase + ";^STARTING TASK";
                stackWindow.FoldRegExTextBox.Items.Add(taskFoldPat);
                stackWindow.FoldRegExTextBox.Items.Add(taskFoldPatBase);

                // If the new pattern is a superset of the old, then use it.  
                if (taskFoldPat.StartsWith(stackWindow.FoldRegExTextBox.Text))
                {
                    stackWindow.FoldRegExTextBox.Text = taskFoldPat;
                }

                stackWindow.GroupRegExTextBox.Items.Insert(0, @"[Nuget] System.%!=>OTHER;Microsoft.%!=>OTHER;mscorlib%=>OTHER;v4.0.30319%\%!=>OTHER;system32\*!=>OTHER;syswow64\*!=>OTHER");

                var excludePat = "LAST_BLOCK";
                stackWindow.ExcludeRegExTextBox.Items.Add(excludePat);
                stackWindow.ExcludeRegExTextBox.Items.Add("LAST_BLOCK;Microsoft.Owin.Host.SystemWeb!*IntegratedPipelineContextStage.BeginEvent;Microsoft.Owin.Host.SystemWeb!*IntegratedPipelineContextStage*RunApp");
                stackWindow.ExcludeRegExTextBox.Text = excludePat;
            }

            if (stackSourceName.StartsWith("CPU") || stackSourceName.Contains("Thread Time"))
            {
                if (m_traceLog != null)
                {
                    stackWindow.ExtraTopStats += " TotalProcs " + m_traceLog.NumberOfProcessors;
                }

                stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
                if (!stackSourceName.Contains("Thread Time"))
                {
                    stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
                }
            }

            if (stackSourceName.StartsWith("Processor"))
            {
                stackWindow.GroupRegExTextBox.Items.Insert(0, "Processor ({%}) Priority ({%})->Priority ($2)");
                stackWindow.GroupRegExTextBox.Items.Insert(0, "Processor ({%}) Priority ({%})->Processor ($1)");
            }

            if (stackSourceName == "Net OS Heap Alloc" || stackSourceName == "Image Load" || stackSourceName == "Disk I/O" ||
                stackSourceName == "File I/O" || stackSourceName == "Exceptions" || stackSourceName == "Managed Load" || stackSourceName.StartsWith("Process")
                || stackSourceName.StartsWith("Virtual") || stackSourceName == "Pinning" || stackSourceName.Contains("Thread Time"))
            {
                stackWindow.CallTreeTab.IsSelected = true;      // start with the call tree view
            }

            if (stackSourceName == "Pinning")
            {
                string defaultFoldPattern = "OBJECT_INSTANCEID;LIVE_FOR";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);
            }

            if (stackSourceName == "Pinning At GC Time")
            {
                string defaultFoldPattern = "^PINNED_FOR;^GC_NUM";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);

                stackWindow.GroupRegExTextBox.Text = "mscorlib.ni!->;system.ni!->;{%}!=>module $1;Generation 0->NonGen2;Generation 1->NonGen2;Type System.Byte[]->Type System.Byte[]";
                stackWindow.ExcludeRegExTextBox.Items.Insert(0, "PinnableBufferCache.CreateNewBuffers");
            }

            if (stackSourceName.Contains("Ref Count"))
            {
                stackWindow.RemoveColumn("IncPercentColumn");
                stackWindow.RemoveColumn("ExcPercentColumn");
            }

            if (stackSourceName.Contains("CCW Ref Count"))
            {
                string defaultFoldPattern = "CCW NewRefCnt;CCW AddRef;CCW Release";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);
                stackWindow.FoldRegExTextBox.Items.Insert(1, "CCW NewRefCnt");
            }

            if ((stackSourceName == "Heap Snapshot Pinning") || (stackSourceName == "Heap Snapshot Pinned Object Allocation"))
            {
                string defaultFoldPattern = "OBJECT_INSTANCE";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);
            }

            if (stackSourceName == "Net OS Heap Alloc" || stackSourceName.StartsWith("GC Heap Net Mem") ||
                stackSourceName.StartsWith("Virtual") || stackSourceName.StartsWith("GC Heap Alloc Ignore Free"))
            {
                stackWindow.ComputeMaxInTopStats = true;
            }

            if (stackSourceName == "Net OS Heap Alloc")
            {
                stackWindow.FoldRegExTextBox.Items.Insert(0, "^Alloc");
            }

            if (stackSourceName.StartsWith("ASP.NET Thread Time"))
            {
                var prev = stackWindow.FoldRegExTextBox.Text;
                if (0 < prev.Length)
                {
                    prev += ";";
                }

                prev += "^Request URL";
                stackWindow.FoldRegExTextBox.Text = prev;
                stackWindow.FoldRegExTextBox.Items.Insert(0, prev);
            }

            if (m_extraTopStats != null)
            {
                stackWindow.ExtraTopStats = m_extraTopStats;
            }

            // Warn the user about the behavior of type name lookup, but only once per user.  
            if (stackSourceName == "Net OS Heap Alloc")
            {
                if (App.ConfigData["WarnedAboutOsHeapAllocTypes"] == null)
                {
                    MessageBox.Show(stackWindow,
                        "Warning: Allocation type resolution only happens on window launch.\r\n" +
                        "Thus if you manually lookup symbols in this view you will get method\r\n" +
                        "names of allocations sites, but to get the type name associated the \r\n" +
                        "allocation site.\r\n" +
                        "\r\n" +
                        "You must close and reopen this window to get the allocation types.\r\n"
                        , "May need to resolve PDBs and reopen.");
                    App.ConfigData["WarnedAboutOsHeapAllocTypes"] = "true";
                }
            }
        }
        public override bool SupportsProcesses { get { return true; } }

        /// <summary>
        /// Find symbols for the simple module name 'simpleModuleName.  If 'processId' is non-zero then only search for modules loaded in that
        /// process, otherwise look systemWide.
        /// </summary>
        public override void LookupSymbolsForModule(string simpleModuleName, TextWriter log, int processId = 0)
        {
            var symReader = GetSymbolReader(log);

            // If we have a process, look the DLL up just there
            var moduleFiles = new Dictionary<int, TraceModuleFile>();
            if (processId != 0)
            {
                var process = m_traceLog.Processes.LastProcessWithID(processId);
                if (process != null)
                {
                    foreach (var loadedModule in process.LoadedModules)
                    {
                        if (string.Compare(loadedModule.Name, simpleModuleName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            moduleFiles[(int)loadedModule.ModuleFile.ModuleFileIndex] = loadedModule.ModuleFile;
                        }
                    }
                }
            }

            // We did not find it, try system-wide
            if (moduleFiles.Count == 0)
            {
                foreach (var moduleFile in m_traceLog.ModuleFiles)
                {
                    if (string.Compare(moduleFile.Name, simpleModuleName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        moduleFiles[(int)moduleFile.ModuleFileIndex] = moduleFile;
                    }
                }
            }

            if (moduleFiles.Count == 0)
            {
                throw new ApplicationException("Could not find module " + simpleModuleName + " in trace.");
            }

            if (moduleFiles.Count > 1)
            {
                log.WriteLine("Found {0} modules with name {1}", moduleFiles.Count, simpleModuleName);
            }

            foreach (var moduleFile in moduleFiles.Values)
            {
                m_traceLog.CodeAddresses.LookupSymbolsForModule(symReader, moduleFile);
            }
        }
        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            var tracelog = GetTraceLog(worker.LogWriter, delegate (bool truncated, int numberOfLostEvents, int eventCountAtTrucation)
            {
                if (!m_notifiedAboutLostEvents)
                {
                    HandleLostEvents(parentWindow, truncated, numberOfLostEvents, eventCountAtTrucation, worker);
                    m_notifiedAboutLostEvents = true;
                }
            });

            // Warn about possible Win8 incompatibility.  
            var logVer = tracelog.OSVersion.Major * 10 + tracelog.OSVersion.Minor;
            if (62 <= logVer)
            {
                var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
                if (ver < 62)       // We are decoding on less than windows 8
                {
                    if (!m_notifiedAboutWin8)
                    {
                        m_notifiedAboutWin8 = true;
                        var versionMismatchWarning = "This trace was captured on Window 8 and is being read\r\n" +
                                                     "on and earlier OS.  If you experience any problems please\r\n" +
                                                     "read the trace on an Windows 8 OS.";
                        worker.LogWriter.WriteLine(versionMismatchWarning);
                        parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
                        {
                            MessageBox.Show(parentWindow, versionMismatchWarning, "Log File Version Mismatch", MessageBoxButton.OK);
                        });
                    }
                }
            }

            var advanced = new PerfViewTreeGroup("Advanced Group");
            var memory = new PerfViewTreeGroup("Memory Group");
            var obsolete = new PerfViewTreeGroup("Old Group");
            var experimental = new PerfViewTreeGroup("Experimental Group");
            m_Children = new List<PerfViewTreeItem>();

            bool hasCPUStacks = false;
            bool hasDllStacks = false;
            bool hasCSwitchStacks = false;
            bool hasReadyThreadStacks = false;
            bool hasHeapStacks = false;
            bool hasGCAllocationTicks = false;
            bool hasExceptions = false;
            bool hasManagedLoads = false;
            bool hasAspNet = false;
            bool hasIis = false;
            bool hasDiskStacks = false;
            bool hasAnyStacks = false;
            bool hasVirtAllocStacks = false;
            bool hasFileStacks = false;
            bool hasTpl = false;
            bool hasTplStacks = false;
            bool hasGCHandleStacks = false;
            bool hasMemAllocStacks = false;
            bool hasJSHeapDumps = false;
            bool hasDotNetHeapDumps = false;
            bool hasWCFRequests = false;
            bool hasCCWRefCountStacks = false;
            bool hasNetNativeCCWRefCountStacks = false;
            bool hasWindowsRefCountStacks = false;
            bool hasPinObjectAtGCTime = false;
            bool hasObjectUpdate = false;
            bool hasGCEvents = false;
            bool hasProjectNExecutionTracingEvents = false;
            bool hasDefenderEvents = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            bool hasJIT = false;

            var stackEvents = new List<TraceEventCounts>();
            foreach (var counts in tracelog.Stats)
            {
                var name = counts.EventName;
                if (!hasCPUStacks && name.StartsWith("PerfInfo"))
                {
                    hasCPUStacks = true;                // Even without true stacks we can display something in the stack viewer.  
                }

                if (!hasAspNet && name.StartsWith("AspNetReq"))
                {
                    hasAspNet = true;
                }

                if (!hasIis && name.StartsWith("IIS"))
                {
                    hasIis = true;
                }

                if (counts.ProviderGuid == ApplicationServerTraceEventParser.ProviderGuid)
                {
                    hasWCFRequests = true;
                }

                if (name.StartsWith("JSDumpHeapEnvelope"))
                {
                    hasJSHeapDumps = true;
                }

                if (name.StartsWith("GC/Start"))
                {
                    hasGCEvents = true;
                }

                if (name.StartsWith("GC/BulkNode"))
                {
                    hasDotNetHeapDumps = true;
                }

                if (name.StartsWith("GC/PinObjectAtGCTime"))
                {
                    hasPinObjectAtGCTime = true;
                }

                if (name.StartsWith("GC/BulkSurvivingObjectRanges") || name.StartsWith("GC/BulkMovedObjectRanges"))
                {
                    hasObjectUpdate = true;
                }

                if (counts.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                {
                    hasTpl = true;
                }

                if (counts.ProviderGuid == MicrosoftAntimalwareEngineTraceEventParser.ProviderGuid)
                {
                    hasDefenderEvents = true;
                }

                if (name.StartsWith("Method/JittingStarted"))
                {
                    hasJIT = true;
                }
                if (name.StartsWith("TypeLoad/Start"))
                {
                    hasTypeLoad = true;
                }
                if (name.StartsWith("Loader/AssemblyLoad"))
                {
                    hasAssemblyLoad = true;
                }

                if (counts.StackCount > 0)
                {
                    hasAnyStacks = true;
                    if (counts.ProviderGuid == ETWClrProfilerTraceEventParser.ProviderGuid && name.StartsWith("ObjectAllocated"))
                    {
                        hasMemAllocStacks = true;
                    }

                    if (name.StartsWith("GC/SampledObjectAllocation"))
                    {
                        hasMemAllocStacks = true;
                    }

                    if (name.StartsWith("GC/CCWRefCountChange"))
                    {
                        hasCCWRefCountStacks = true;
                    }

                    if (name.StartsWith("TaskCCWRef"))
                    {
                        hasNetNativeCCWRefCountStacks = true;
                    }

                    if (name.StartsWith("Object/CreateHandle"))
                    {
                        hasWindowsRefCountStacks = true;
                    }

                    if (name.StartsWith("Image"))
                    {
                        hasDllStacks = true;
                    }

                    if (name.StartsWith("HeapTrace"))
                    {
                        hasHeapStacks = true;
                    }

                    if (name.StartsWith("Thread/CSwitch"))
                    {
                        hasCSwitchStacks = true;
                    }

                    if (name.StartsWith("GC/AllocationTick"))
                    {
                        hasGCAllocationTicks = true;
                    }

                    if (name.StartsWith("Exception") || name.StartsWith("PageFault/AccessViolation"))
                    {
                        hasExceptions = true;
                    }

                    if (name.StartsWith("GC/SetGCHandle"))
                    {
                        hasGCHandleStacks = true;
                    }

                    if (name.StartsWith("Loader/ModuleLoad"))
                    {
                        hasManagedLoads = true;
                    }

                    if (name.StartsWith("VirtualMem"))
                    {
                        hasVirtAllocStacks = true;
                    }

                    if (name.StartsWith("Dispatcher/ReadyThread"))
                    {
                        hasReadyThreadStacks = true;
                    }

                    if (counts.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                    {
                        hasTplStacks = true;
                    }

                    if (name.StartsWith("DiskIO"))
                    {
                        hasDiskStacks = true;
                    }

                    if (name.StartsWith("FileIO"))
                    {
                        hasFileStacks = true;
                    }

                    if (name.StartsWith("MethodEntry"))
                    {
                        hasProjectNExecutionTracingEvents = true;
                    }
                }
            }

            m_Children.Add(new PerfViewTraceInfo(this));
            m_Children.Add(new PerfViewProcesses(this));

            m_Children.Add(new PerfViewStackSource(this, "Processes / Files / Registry") { SkipSelectProcess = true });

            if (hasCPUStacks)
            {
                m_Children.Add(new PerfViewStackSource(this, "CPU"));
                experimental.Children.Add(new AutomatedAnalysisReport(this));
                if (!App.CommandLineArgs.ShowOptimizationTiers &&
                    tracelog.Events.Any(
                        e => e is MethodLoadUnloadTraceDataBase td && td.OptimizationTier != OptimizationTier.Unknown))
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "CPU (with Optimization Tiers)"));
                }
                advanced.Children.Add(new PerfViewStackSource(this, "Processor"));
            }

            if (hasCSwitchStacks)
            {
                if (hasTplStacks)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with Tasks)"));
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities)"));
                }
                else
                {
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time"));
                }

                if (hasReadyThreadStacks)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with ReadyThread)"));
                }
            }
            else if (hasCPUStacks && hasTplStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities) (CPU ONLY)"));
            }

            if (hasDiskStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Disk I/O"));
            }

            if (hasFileStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "File I/O"));
            }

            if (hasHeapStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Net OS Heap Alloc"));
            }

            if (hasVirtAllocStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Net Virtual Alloc"));
                memory.Children.Add(new PerfViewStackSource(this, "Net Virtual Reserve"));
            }
            if (hasGCAllocationTicks)
            {
                if (hasObjectUpdate)
                {
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem (Coarse Sampling)"));
                    memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths (Coarse Sampling)"));
                }
                memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free (Coarse Sampling)"));
            }
            if (hasMemAllocStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem"));
                memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free"));
                memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths"));
            }

            if (hasDllStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Image Load"));
            }

            if (hasManagedLoads)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Managed Load"));
            }

            if (hasExceptions)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Exceptions"));
            }

            if (hasGCHandleStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Pinning"));
            }

            if (hasPinObjectAtGCTime)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Pinning At GC Time"));
            }

            if (hasGCEvents && hasCPUStacks && AppLog.InternalUser)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Server GC"));
            }

            if (hasCCWRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "CCW Ref Count"));
            }

            if (hasNetNativeCCWRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, ".NET Native CCW Ref Count"));
            }

            if (hasWindowsRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Windows Handle Ref Count"));
            }

            if (hasGCHandleStacks && hasMemAllocStacks)
            {
                bool matchingHeapSnapshotExists = GCPinnedObjectAnalyzer.ExistsMatchingHeapSnapshot(FilePath);
                if (matchingHeapSnapshotExists)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Heap Snapshot Pinning"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Heap Snapshot Pinned Object Allocation"));
                }
            }

            if ((hasAspNet) || (hasWCFRequests))
            {
                if (hasCPUStacks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request CPU"));
                }
                if (hasCSwitchStacks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request Thread Time"));
                }
                if (hasGCAllocationTicks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request Managed Allocation"));
                }
            }

            if (hasAnyStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Any"));
                if (hasTpl)
                {
                    if (hasCSwitchStacks)
                    {
                        advanced.Children.Add(new PerfViewStackSource(this, "Any Stacks (with Tasks)"));
                        advanced.Children.Add(new PerfViewStackSource(this, "Any Stacks (with StartStop Activities)"));
                        advanced.Children.Add(new PerfViewStackSource(this, "Any StartStopTree"));
                    }
                    advanced.Children.Add(new PerfViewStackSource(this, "Any TaskTree"));
                }
            }

            if (hasAspNet)
            {
                advanced.Children.Add(new PerfViewAspNetStats(this));
                if (hasCPUStacks)
                {
                    var name = "ASP.NET Thread Time";
                    if (hasCSwitchStacks && hasTplStacks)
                    {
                        obsolete.Children.Add(new PerfViewStackSource(this, "ASP.NET Thread Time (with Tasks)"));
                    }
                    else if (!hasCSwitchStacks)
                    {
                        name += " (CPU ONLY)";
                    }

                    obsolete.Children.Add(new PerfViewStackSource(this, name));
                }
            }

            if (hasIis)
            {
                advanced.Children.Add(new PerfViewIisStats(this));
            }

            if (hasProjectNExecutionTracingEvents && AppLog.InternalUser)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Execution Tracing"));
            }

            if(hasDefenderEvents)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Anti-Malware Real-Time Scan"));
            }

            memory.Children.Add(new PerfViewGCStats(this));

            // TODO currently this is experimental enough that we don't show it publicly.  
            if (AppLog.InternalUser)
            {
                memory.Children.Add(new MemoryAnalyzer(this));
            }

            if (hasJSHeapDumps || hasDotNetHeapDumps)
            {
                memory.Children.Add(new PerfViewHeapSnapshots(this));
            }

            advanced.Children.Add(new PerfViewJitStats(this));

            if (hasJIT || hasAssemblyLoad || hasTypeLoad)
            {
                advanced.Children.Add(new PerfViewRuntimeLoaderStats(this));
            }

            advanced.Children.Add(new PerfViewEventStats(this));

            m_Children.Add(new PerfViewEventSource(this));

            if (0 < memory.Children.Count)
            {
                m_Children.Add(memory);
            }

            if (0 < advanced.Children.Count)
            {
                m_Children.Add(advanced);
            }

            if (0 < obsolete.Children.Count)
            {
                m_Children.Add(obsolete);
            }

            if (AppLog.InternalUser && 0 < experimental.Children.Count)
            {
                m_Children.Add(experimental);
            }

            return null;
        }
        // public override string DefaultStackSourceName { get { return "CPU"; } }

        public TraceLog GetTraceLog(TextWriter log, Action<bool, int, int> onLostEvents = null)
        {
            if (m_traceLog != null)
            {
                if (IsUpToDate)
                {
                    return m_traceLog;
                }

                m_traceLog.Dispose();
                m_traceLog = null;
            }
            var dataFileName = FilePath;
            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            var traceEventDispatcherOptions = new TraceEventDispatcherOptions();
            traceEventDispatcherOptions.StartTime = App.CommandLineArgs.StartTime;
            traceEventDispatcherOptions.EndTime = App.CommandLineArgs.EndTime;

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // But if there is a directory called EtwManifests exists, look in there instead. 
            var etwManifestDirPath = Path.Combine(Path.GetDirectoryName(dataFileName), "EtwManifests");
            if (Directory.Exists(etwManifestDirPath))
            {
                options.ExplicitManifestDir = etwManifestDirPath;
            }

            CommandProcessor.UnZipIfNecessary(ref dataFileName, log);

            var etlxFile = dataFileName;
            var cachedEtlxFile = false;
            if (dataFileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) || dataFileName.EndsWith(".btl", StringComparison.OrdinalIgnoreCase))
            {
                etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
                if (!File.Exists(etlxFile))
                {
                    log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                    TraceLog.CreateFromEventTraceLogFile(dataFileName, etlxFile, options, traceEventDispatcherOptions);

                    var dataFileSize = "Unknown";
                    if (File.Exists(dataFileName))
                    {
                        dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
                    }

                    log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);
                }
                else
                {
                    cachedEtlxFile = true;
                    log.WriteLine("Found a corresponding ETLX file {0}", etlxFile);
                }
            }

            try
            {
                m_traceLog = new TraceLog(etlxFile);

                // Add some more parser that we would like.  
                new ETWClrProfilerTraceEventParser(m_traceLog);
                new MicrosoftWindowsNDISPacketCaptureTraceEventParser(m_traceLog);
            }
            catch (Exception)
            {
                if (cachedEtlxFile)
                {
                    // Delete the file and try again.  
                    FileUtilities.ForceDelete(etlxFile);
                    if (!File.Exists(etlxFile))
                    {
                        return GetTraceLog(log, onLostEvents);
                    }
                }
                throw;
            }

            m_utcLastWriteAtOpen = File.GetLastWriteTimeUtc(FilePath);
            if (App.CommandLineArgs.UnsafePDBMatch)
            {
                m_traceLog.CodeAddresses.UnsafePDBMatching = true;
            }

            if (m_traceLog.Truncated)   // Warn about truncation.  
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show("The ETL file was too big to convert and was truncated.\r\nSee log for details", "Log File Truncated", MessageBoxButton.OK);
                });
            }
            return m_traceLog;
        }
        public TraceLog TryGetTraceLog() { return m_traceLog; }

        private void HandleLostEvents(Window parentWindow, bool truncated, int numberOfLostEvents, int eventCountAtTrucation, StatusBar worker)
        {
            string warning;
            if (!truncated)
            {
                // TODO see if we can get the buffer size out of the ETL file to give a good number in the message. 
                warning = "WARNING: There were " + numberOfLostEvents + " lost events in the trace.\r\n" +
                          "Some analysis might be invalid.\r\n" +
                          "Use /InMemoryCircularBuffer or /BufferSize:1024 to avoid this in future traces.";
            }
            else
            {
                warning = "WARNING: The ETLX file was truncated at " + eventCountAtTrucation + " events.\r\n" +
                          "This is to keep the ETLX file size under 4GB, however all rundown events are processed.\r\n" +
                          "Use /SkipMSec:XXX after clearing the cache (File->Clear Temp Files) to see the later parts of the file.\r\n" +
                          "See log for more details.";
            }

            MessageBoxResult result = MessageBoxResult.None;
            parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                result = MessageBox.Show(parentWindow, warning, "Lost Events", MessageBoxButton.OKCancel);
                worker.LogWriter.WriteLine(warning);
                if (result != MessageBoxResult.OK)
                {
                    worker.AbortWork();
                }
            });
        }
        public override void Close()
        {
            if (m_traceLog != null)
            {
                m_traceLog.Dispose();
                m_traceLog = null;
            }
            base.Close();
        }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        internal static List<TraceModuleFile> GetInterestingModuleFiles(ETLPerfViewData etlFile, double pdbThresholdPercent, TextWriter log, List<int> focusProcessIDs = null)
        {
            // If a DLL is loaded into multiple processes or at different locations we can get repeats, strip them.  
            var ret = new List<TraceModuleFile>();
            var traceLog = etlFile.GetTraceLog(log);

            // There can be several TraceModuleFile for a given path because the module is loaded more than once.
            // Thus we need to accumulate the counts.  This is what moduleCodeAddressCounts does 
            var moduleCodeAddressCounts = new Dictionary<string, int>();
            // Get symbols in cache, generate NGEN images if necessary.  

            IEnumerable<TraceModuleFile> moduleList = traceLog.ModuleFiles;
            int totalCpu = traceLog.CodeAddresses.TotalCodeAddresses;
            if (focusProcessIDs != null)
            {
                var processtotalCpu = 0;
                var processModuleList = new List<TraceModuleFile>();
                foreach (var process in traceLog.Processes)
                {
                    processtotalCpu += (int)process.CPUMSec;
                    if (!focusProcessIDs.Contains(process.ProcessID))
                    {
                        continue;
                    }

                    log.WriteLine("Restricting to process {0} ({1})", process.Name, process.ProcessID);
                    foreach (var mod in process.LoadedModules)
                    {
                        processModuleList.Add(mod.ModuleFile);
                    }
                }
                if (processtotalCpu != 0 && processModuleList.Count > 0)
                {
                    totalCpu = processtotalCpu;
                    moduleList = processModuleList;
                }
                else
                {
                    log.WriteLine("ERROR: could not find any CPU in focus processes, using machine wide total.");
                }
            }
            log.WriteLine("Total CPU = {0} samples", totalCpu);
            int pdbThreshold = (int)((pdbThresholdPercent * totalCpu) / 100.0);
            log.WriteLine("Pdb threshold = {0:f2}% = {1} code address instances", pdbThresholdPercent, pdbThreshold);

            foreach (var moduleFile in moduleList)
            {
                if (moduleFile.CodeAddressesInModule == 0)
                {
                    continue;
                }

                int count = 0;
                if (moduleCodeAddressCounts.TryGetValue(moduleFile.FilePath, out count))
                {
                    // We have already hit the threshold so we don't need to do anything. 
                    if (count >= pdbThreshold)
                    {
                        continue;
                    }
                }

                count += moduleFile.CodeAddressesInModule;
                moduleCodeAddressCounts[moduleFile.FilePath] = count;
                if (count < pdbThreshold)
                {
                    continue;                   // Have not reached threshold
                }

                log.WriteLine("Addr Count = {0} >= {1}, adding: {2}", count, pdbThreshold, moduleFile.FilePath);
                ret.Add(moduleFile);
            }
            return ret;
        }



        #region private
        /// <summary>
        /// See if the log has events from VS providers.  If so we should register the VS providers. 
        /// </summary>
        private bool HasVSEvents(TraceLog traceLog)
        {
            if (!m_checkedForVSEvents)
            {
                var codeMarkerGuid = new Guid(0x143A31DB, 0x0372, 0x40B6, 0xB8, 0xF1, 0xB4, 0xB1, 0x6A, 0xDB, 0x5F, 0x54);
                var measurementBlockGuid = new Guid(0x641D7F6C, 0x481C, 0x42E8, 0xAB, 0x7E, 0xD1, 0x8D, 0xC5, 0xE5, 0xCB, 0x9E);
                foreach (var stats in traceLog.Stats)
                {
                    if (stats.ProviderGuid == codeMarkerGuid || stats.ProviderGuid == measurementBlockGuid)
                    {
                        m_hasVSEvents = true;
                        break;
                    }
                }

                m_checkedForVSEvents = true;
            }
            return m_hasVSEvents;
        }

        private bool m_checkedForVSEvents;
        private bool m_hasVSEvents;
        private TraceLog m_traceLog;
        private bool m_notifiedAboutLostEvents;
        private bool m_notifiedAboutWin8;
        private string m_extraTopStats;
        #endregion
    }
}