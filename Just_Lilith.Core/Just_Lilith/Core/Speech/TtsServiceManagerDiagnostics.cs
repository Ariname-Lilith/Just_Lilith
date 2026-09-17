using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Just_Lilith.Core.Speech;

internal sealed class TtsServiceManagerDiagnostics : IDisposable
{
	internal const int MaximumFileBytes = 262144;

	private readonly object gate = new object();

	private readonly string path;

	private readonly string previous;

	private FileStream? stream;

	private bool disposed;

	internal bool WriteFailed { get; private set; }

	internal TtsServiceManagerDiagnostics(string runtimeDirectory)
	{
		string path = Path.Combine(runtimeDirectory, "logs");
		this.path = Path.Combine(path, "managed-service.log");
		previous = this.path + ".1";
		TtsServiceManagerProcess.AssertPlainPath(this.path);
		TtsServiceManagerProcess.AssertPlainPath(previous);
		Directory.CreateDirectory(path);
		TtsServiceManagerProcess.AssertPlainPath(path);
		Open();
	}

	private void Open()
	{
		stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096);
	}

	internal void Write(string text, int? pid = null)
	{
		lock (gate)
		{
			if (disposed || WriteFailed)
			{
				return;
			}
			try
			{
				text = SanitizeText(text);
				text = new string(text.Where((char c) => !char.IsControl(c) || c == '\t').Take(1800).ToArray());
				byte[] bytes = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O") + " pid=" + (pid?.ToString() ?? "-") + " " + text + Environment.NewLine);
				if (stream.Length + bytes.Length > 262144)
				{
					stream.Flush();
					stream.Dispose();
					stream = null;
					TtsServiceManagerProcess.AssertPlainPath(path);
					TtsServiceManagerProcess.AssertPlainPath(previous);
					File.Move(path, previous, overwrite: true);
					Open();
				}
				stream.Write(bytes);
				stream.Flush();
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				WriteFailed = true;
			}
		}
	}

	internal static string SanitizeText(string text)
	{
		text = Regex.Replace(text, "(?i)([\"']?(?:authorization|api[_-]?key|access[_-]?token|instance[_-]?token|password)[\"']?\\s*[:=]\\s*)(?:\"[^\"]*\"|'[^']*'|(?:Bearer\\s+)?[^\\s,;]+)", "$1[redacted]");
		return Regex.Replace(text, "(?i)\\bBearer\\s+[^\\s\"',;]+", "Bearer [redacted]");
	}

	public void Dispose()
	{
		lock (gate)
		{
			if (!disposed)
			{
				disposed = true;
				try
				{
					stream?.Flush();
				}
				catch (IOException)
				{
					WriteFailed = true;
				}
				stream?.Dispose();
				stream = null;
			}
		}
	}
}
