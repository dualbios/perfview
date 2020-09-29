﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using EventView.Dialogs;

namespace EventView.FileFormats
{
    public class FileFormatFactory : IFileFormatFactory
    {
        private readonly IEnumerable<IFileFormat> _fileFormats;

        public FileFormatFactory(IEnumerable<IFileFormat> fileFormats)
        {
            _fileFormats = fileFormats;
        }

        public IFileFormat Get(string fileName)
        {
            string fileExtension = Path.GetExtension(fileName);
            return _fileFormats.FirstOrDefault(x => x.FileExtensions.Contains(fileExtension));
        }

        public void Init(IDialogPlaceHolder dialogPlaceHolder)
        {
            foreach (IFileFormat fileFormat in _fileFormats)
            {
                fileFormat.Init(dialogPlaceHolder);
            }
        }
    }
}