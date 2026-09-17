using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Just_Lilith.Core.Llm;

public sealed class WindowsSecretProtector : ISecretProtector
{
	private struct Blob
	{
		public int Length;

		public IntPtr Data;
	}

	private const uint UiForbidden = 1u;

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);

	[DllImport("kernel32.dll")]
	private static extern IntPtr LocalFree(IntPtr memory);

	public string Protect(string plaintext)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
		try
		{
			return Convert.ToBase64String(Transform(bytes, protect: true));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
		}
	}

	public string Unprotect(string protectedText)
	{
		byte[] array;
		try
		{
			array = Convert.FromBase64String(protectedText);
		}
		catch (FormatException)
		{
			throw KeyError();
		}
		try
		{
			byte[] array2 = Transform(array, protect: false);
			try
			{
				return Encoding.UTF8.GetString(array2);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(array2);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
		}
	}

	private static byte[] Transform(byte[] bytes, bool protect)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new LlmException("key_storage", "API Key 加密存储需要 Windows 当前用户环境。");
		}
		Blob input = new Blob
		{
			Length = bytes.Length,
			Data = Marshal.AllocHGlobal(bytes.Length)
		};
		Blob output = default(Blob);
		try
		{
			Marshal.Copy(bytes, 0, input.Data, bytes.Length);
			if (!(protect ? CryptProtectData(ref input, "Just_Lilith API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1u, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1u, out output)))
			{
				throw KeyError();
			}
			if (output.Length < 0 || output.Length > 65536)
			{
				throw KeyError();
			}
			byte[] array = new byte[output.Length];
			Marshal.Copy(output.Data, array, 0, array.Length);
			return array;
		}
		finally
		{
			if (input.Data != IntPtr.Zero)
			{
				Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
				Marshal.FreeHGlobal(input.Data);
			}
			if (output.Data != IntPtr.Zero)
			{
				if (output.Length > 0 && output.Length <= 65536)
				{
					Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
				}
				LocalFree(output.Data);
			}
		}
	}

	private static LlmException KeyError()
	{
		return new LlmException("key_storage", "API Key 加密或解密失败；请在当前 Windows 账号下重新输入并保存。");
	}
}
