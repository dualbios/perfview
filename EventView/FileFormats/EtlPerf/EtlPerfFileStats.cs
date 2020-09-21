namespace EventView.FileFormats.EtlPerf
{
    public class EtlPerfFileStats
    {
        public bool HasAnyStacks { get; set; }
        public bool HasAspNet { get; set; }
        public bool HasAssemblyLoad { get; set; }
        public bool HasCCWRefCountStacks { get; set; }
        public bool HasCPUStacks { get; set; }
        public bool HasCSwitchStacks { get; set; }
        public bool HasDefenderEvents { get; set; }
        public bool HasDiskStacks { get; set; }
        public bool HasDllStacks { get; set; }
        public bool HasDotNetHeapDumps { get; set; }
        public bool HasExceptions { get; set; }
        public bool HasFileStacks { get; set; }
        public bool HasGCAllocationTicks { get; set; }
        public bool HasGCEvents { get; set; }
        public bool HasGCHandleStacks { get; set; }
        public bool HasHeapStacks { get; set; }
        public bool HasIis { get; set; }
        public bool HasJIT { get; set; }
        public bool HasJSHeapDumps { get; set; }
        public bool HasManagedLoads { get; set; }
        public bool HasMemAllocStacks { get; set; }
        public bool HasNetNativeCCWRefCountStacks { get; set; }
        public bool HasObjectUpdate { get; set; }
        public bool HasPinObjectAtGCTime { get; set; }
        public bool HasProjectNExecutionTracingEvents { get; set; }
        public bool HasReadyThreadStacks { get; set; }
        public bool HasTpl { get; set; }
        public bool HasTplStacks { get; set; }
        public bool HasTypeLoad { get; set; }
        public bool HasVirtAllocStacks { get; set; }
        public bool HasWCFRequests { get; set; }
        public bool HasWindowsRefCountStacks { get; set; }
    }
}