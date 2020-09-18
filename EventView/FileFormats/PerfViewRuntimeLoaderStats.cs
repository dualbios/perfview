﻿using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewRuntimeLoaderStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewRuntimeLoaderStats(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; }

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}