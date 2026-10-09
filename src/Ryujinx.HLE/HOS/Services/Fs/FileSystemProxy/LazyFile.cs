using LibHac;
using LibHac.Common;
using LibHac.Fs;
using System;
using System.IO;

namespace Ryujinx.HLE.HOS.Services.Fs.FileSystemProxy
{
    class LazyFile : LibHac.Fs.Fsa.IFile
    {
        private readonly LibHac.Fs.Fsa.IFileSystem _fs;
        private readonly string _filePath;
        private readonly UniqueRef<LibHac.Fs.Fsa.IFile> _fileReference = new();
        private readonly FileInfo _fileInfo;

        public LazyFile(string filePath, string prefix, LibHac.Fs.Fsa.IFileSystem fs)
        {
            _fs = fs;
            _filePath = filePath;
            _fileInfo = new FileInfo(prefix + "/" + filePath);
        }

        private void PrepareFile()
        {
            if (_fileReference.Get == null)
            {
                _fs.OpenFile(ref _fileReference.Ref, _filePath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
            }
        }

        // [Nextendo, HOME Menu work] A mod file is read whole into memory, and its handle closed:
        // kept open, the file could not be removed while the program runs (NXThemes Installer
        // failed to uninstall the theme of the running HOME Menu: "cannot remove: I/O error").
        private byte[] _content;

        protected override Result DoRead(out long bytesRead, long offset, Span<byte> destination, in ReadOption option)
        {
            if (_content == null && _fileInfo.Exists && _fileInfo.Length <= 64 * 1024 * 1024)
            {
                try
                {
                    _content = File.ReadAllBytes(_fileInfo.FullName);
                }
                catch (IOException)
                {
                    _content = null;
                }
            }

            if (_content != null)
            {
                int count = (int)Math.Clamp(_content.Length - offset, 0, destination.Length);

                _content.AsSpan((int)Math.Min(offset, _content.Length), count).CopyTo(destination);
                bytesRead = count;

                return Result.Success;
            }

            PrepareFile();

            return _fileReference.Get!.Read(out bytesRead, offset, destination);
        }

        protected override Result DoWrite(long offset, ReadOnlySpan<byte> source, in WriteOption option)
        {
            throw new NotSupportedException();
        }

        protected override Result DoFlush()
        {
            throw new NotSupportedException();
        }

        protected override Result DoSetSize(long size)
        {
            throw new NotSupportedException();
        }

        protected override Result DoGetSize(out long size)
        {
            size = _fileInfo.Length;

            return Result.Success;
        }

        protected override Result DoOperateRange(Span<byte> outBuffer, OperationId operationId, long offset, long size, ReadOnlySpan<byte> inBuffer)
        {
            throw new NotSupportedException();
        }
    }
}
