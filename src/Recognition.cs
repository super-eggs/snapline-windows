using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Snapline
{
    [DataContract]
    internal sealed class RecognitionResult
    {
        [DataMember(Name = "lines")] internal RecognitionLine[] Lines { get; set; }
        [DataMember(Name = "error")] internal string Error { get; set; }
    }

    [DataContract]
    internal sealed class RecognitionLine
    {
        [DataMember(Name = "text")] internal string Text { get; set; }
        [DataMember(Name = "box")] internal double[][] Box { get; set; }
        [DataMember(Name = "glyphs")] internal RecognitionGlyph[] Glyphs { get; set; }
    }

    [DataContract]
    internal sealed class RecognitionGlyph
    {
        [DataMember(Name = "text")] internal string Text { get; set; }
        [DataMember(Name = "box")] internal double[][] Box { get; set; }
    }

    internal sealed class Recognition
    {
        internal const string MissingComponent = "文字识别组件缺失，请重新下载文字识别版。";
#if SNAPLINE_OCR
        internal static readonly bool Included = true;
#else
        internal static readonly bool Included = false;
#endif
        private readonly string folder;

        private Recognition(string folder) { this.folder = folder; }

        internal static Recognition Find()
        {
            if (!Included) return null;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recognition");
            return File.Exists(Path.Combine(folder, "protocol-v2")) && File.Exists(Path.Combine(folder, "python.exe"))
                && File.Exists(Path.Combine(folder, "recognize.py")) ? new Recognition(folder) : null;
        }

        internal Task<RecognitionLine[]> ReadAsync(BitmapSource image, CancellationToken cancellation)
        {
            return Task.Run(async delegate
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var process = new Process())
                {
                    deadline.CancelAfter(TimeSpan.FromMinutes(1));
                    deadline.Token.ThrowIfCancellationRequested();
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(folder, "python.exe"), Arguments = "-I -B recognize.py",
                        WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                    };
                    process.Start();
                    using (deadline.Token.Register(delegate
                    {
                        try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception) { }
                    }))
                    {
                        Task<string> output = process.StandardOutput.ReadToEndAsync();
                        Task<string> errors = process.StandardError.ReadToEndAsync();
                        try
                        {
                            byte[] png = Store.Encode(image);
                            cancellation.ThrowIfCancellationRequested();
                            using (Stream input = process.StandardInput.BaseStream) input.Write(png, 0, png.Length);
                        }
                        catch (IOException)
                        {
                            // A failed or cancelled worker may close its pipe before reading the image.
                            if (!process.HasExited && !deadline.IsCancellationRequested) throw;
                        }
                        string response = await output.ConfigureAwait(false);
                        await errors.ConfigureAwait(false);
                        process.WaitForExit();
                        cancellation.ThrowIfCancellationRequested();
                        if (deadline.IsCancellationRequested) throw new TimeoutException("识别超时，请缩小图片范围后重试。");
                        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(response))
                            throw new InvalidDataException("无法启动文字识别，请重新下载文字识别版。");
                        using (var data = new MemoryStream(Encoding.UTF8.GetBytes(response)))
                        {
                            var serializer = new DataContractJsonSerializer(typeof(RecognitionResult),
                                new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 1000000 });
                            var result = (RecognitionResult)serializer.ReadObject(data);
                            if (result == null || result.Lines == null || !string.IsNullOrEmpty(result.Error))
                                throw new InvalidDataException("文字识别失败，请缩小图片范围后重试。");
                            return result.Lines;
                        }
                    }
                }
            }, cancellation);
        }
    }
}
