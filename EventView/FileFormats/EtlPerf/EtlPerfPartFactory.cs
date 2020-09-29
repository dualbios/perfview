using System.Collections.Generic;
using System.Linq;
using EventView.Dialogs;
using EventView.FileFormats.EtlPerf.Parts;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace EventView.FileFormats.EtlPerf
{
    public class EtlPerfPartFactory : IEtlPerfPartFactory
    {
        private IDialogPlaceHolder _dialogPlaceHolder;

       
        public EtlPerfFileStats CreateStats(TraceEventStats stats)
        {
            EtlPerfFileStats fileStats = new EtlPerfFileStats
            {
                HasCPUStacks = stats.Any(x => x.EventName.StartsWith("PerfInfo")),
                HasAspNet = stats.Any(x => x.EventName.StartsWith("AspNetReq")),

                HasIis = stats.Any(x => x.EventName.StartsWith("IIS")),

                HasWCFRequests = stats.Any(x => x.ProviderGuid == ApplicationServerTraceEventParser.ProviderGuid),

                HasJSHeapDumps = stats.Any(x => x.EventName.StartsWith("JSDumpHeapEnvelope")),

                HasGCEvents = stats.Any(x => x.EventName.StartsWith("GC/Start")),
                HasDotNetHeapDumps = stats.Any(x => x.EventName.StartsWith("GC/BulkNode")),
                HasPinObjectAtGCTime = stats.Any(x => x.EventName.StartsWith("GC/PinObjectAtGCTim")),

                HasObjectUpdate = stats.Any(x => x.EventName.StartsWith("GC/BulkSurvivingObjectRange") || x.EventName.StartsWith("GC/BulkMovedObjectRanges")),

                HasTpl = stats.Any(x => x.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid),
                HasDefenderEvents = stats.Any(x => x.ProviderGuid == MicrosoftAntimalwareEngineTraceEventParser.ProviderGuid),

                HasJIT = stats.Any(x => x.EventName.StartsWith("Method/JittingStarted")),

                HasTypeLoad = stats.Any(x => x.EventName.StartsWith("TypeLoad/Start")),
                HasAssemblyLoad = stats.Any(x => x.EventName.StartsWith("Loader/AssemblyLoad")),
                HasAnyStacks = stats.Any(x => x.StackCount > 0),
                HasMemAllocStacks = stats.Where(x => x.StackCount > 0)
                    .Any(x => x.ProviderGuid == ETWClrProfilerTraceEventParser.ProviderGuid && x.EventName.StartsWith("ObjectAllocated")
                              || x.EventName.StartsWith("GC/SampledObjectAllocation")),

                HasCCWRefCountStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("GC/CCWRefCountChange")),

                HasNetNativeCCWRefCountStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("TaskCCWRef")),

                HasWindowsRefCountStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("Object/CreateHandl")),
                HasDllStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("Image")),
                HasHeapStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("HeapTrace")),

                HasCSwitchStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("Thread/CSwitch")),
                HasGCAllocationTicks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("GC/AllocationTick")),

                HasExceptions = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("Exception") || x.EventName.StartsWith("PageFault/AccessViolation")),

                HasGCHandleStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("GC/SetGCHandle")),
                HasManagedLoads = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("Loader/ModuleLoad")),
                HasVirtAllocStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("VirtualMem")),
                HasReadyThreadStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("Dispatcher/ReadyThread")),
                HasTplStacks = stats.Where(x => x.StackCount > 0).Any(x => x.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid),

                HasDiskStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("DiskIO")),
                HasFileStacks = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("FileIO")),
                HasProjectNExecutionTracingEvents = stats.Where(x => x.StackCount > 0).Any(x => x.EventName.StartsWith("MethodEntry")),
            };

            return fileStats;
        }

        public IEnumerable<IEtlFilePart> GetParts(EtlPerfFileStats stats)
        {
            foreach (IEtlFilePart part in GetSupportedPart())
            {
                if (part.IsExist(stats))
                    yield return part;
            }
        }

        public IEnumerable<IEtlFilePart> GetSupportedPart()
        {
            yield return new ProcessesFilesRegistryFilePart();

            yield return new PerfViewTraceInfo();
            yield return new PerfViewProcesses();

            yield return new ThreadTimeFilePart();
            yield return new ThreadTimeWithTasksFilePart();
            yield return new ThreadTimeWithStartStopActivities();
            yield return new ThreadTimeWithReadyThread();
            yield return new ThreadTimeWithStartStopActivitiesCPUONLY();

            yield return new DiskIOFilePart();
            yield return new FileIOFilePart();

            yield return new NetOSHeapAlloc();
            yield return new NetVirtualAlloc();
            yield return new NetVirtualReserve();

            yield return new GCHeapNetMemCoarseSampling();
            yield return new Gen2ObjectDeathsCoarseSampling();
            yield return new GCHeapAllocIgnoreFreeCoarseSampling(_dialogPlaceHolder);

            yield return new GCHeapNetMem();
            yield return new GCHeapAllocIgnoreFree();
            yield return new Gen2ObjectDeaths();

            yield return new ImageLoad();
            yield return new ManagedLoad();
            yield return new Exceptions();
            yield return new Pinning();

            yield return new PerfViewEventStats();
            yield return new PerfViewEventSource();
        }

        public void Init(IDialogPlaceHolder dialogPlaceHolder)
        {
            _dialogPlaceHolder = dialogPlaceHolder;
        }
    }
}