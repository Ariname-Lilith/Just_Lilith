using System;
using System.Buffers.Binary;
using System.Linq;

namespace Just_Lilith.Core.Speech;

public static class PcmWaveReader
{
	public const int MaximumBytes = 33554432;

	public const int MaximumDurationSeconds = 180;

	private static readonly int[] Rates = new int[10] { 8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 88200, 96000 };

	public static PcmWaveData Read(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length > 33554432)
		{
			throw Error("wave_limit", "TTS 音频超过 32 MiB 限制。");
		}
		if (bytes.Length < 44 || !Tag(bytes, 0, "RIFF") || !Tag(bytes, 8, "WAVE") || (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8L != bytes.Length)
		{
			throw Error("wave_format", "TTS 音频的 RIFF/WAVE 文件头或长度不正确。");
		}
		int num = 12;
		bool flag = false;
		bool flag2 = false;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		ulong num8;
		for (; num < bytes.Length; num = (int)num8)
		{
			if (bytes.Length - num < 8)
			{
				throw Error("wave_format", "TTS 音频包含不完整的块头。");
			}
			uint num7 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(num + 4, 4));
			num8 = (ulong)((long)num + 8L + num7 + (num7 & 1));
			if (num8 > (ulong)bytes.Length)
			{
				throw Error("wave_format", "TTS 音频块长度越界。");
			}
			int num9 = num + 8;
			if (Tag(bytes, num, "fmt "))
			{
				if (!flag)
				{
					switch (num7)
					{
					case 16u:
					case 18u:
					{
						ReadOnlySpan<byte> source = bytes.Slice(num9, (int)num7);
						if (BinaryPrimitives.ReadUInt16LittleEndian(source) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(14)) != 16 || (num7 == 18 && BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(16)) != 0))
						{
							throw Error("wave_format", "TTS 音频须为未压缩的 16 位 PCM WAV。");
						}
						num2 = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(2));
						uint num10 = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4));
						bool flag3 = num10 > int.MaxValue;
						if (!flag3)
						{
							bool flag4 = (uint)(num2 - 1) <= 1u;
							flag3 = !flag4;
						}
						if (flag3 || !Enumerable.Contains(Rates, (int)num10))
						{
							throw Error("wave_format", "TTS 音频采样率或声道数不受支持。");
						}
						num3 = (int)num10;
						num4 = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(12));
						if (num4 != num2 * 2 || BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8)) != (uint)(num3 * num4))
						{
							throw Error("wave_format", "TTS 音频帧宽或字节率不一致。");
						}
						flag = true;
						continue;
					}
					}
				}
				throw Error("wave_format", "TTS 音频格式块重复或结构不正确。");
			}
			if (Tag(bytes, num, "data"))
			{
				if ((!flag | flag2) || num7 == 0)
				{
					throw Error("wave_format", "TTS 音频数据块为空、重复或顺序不正确。");
				}
				num5 = num9;
				num6 = (int)num7;
				flag2 = true;
			}
		}
		if (!flag || !flag2 || num6 % num4 != 0)
		{
			throw Error("wave_format", "TTS 音频缺少必要块或包含不完整的采样帧。");
		}
		if (num6 / num4 > (long)num3 * 180L)
		{
			throw Error("wave_limit", "TTS 音频超过 180 秒限制。");
		}
		float[] array = new float[num6 / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = (float)BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(num5 + i * 2, 2)) / 32768f;
		}
		return new PcmWaveData(num3, num2, array);
	}

	private static bool Tag(ReadOnlySpan<byte> value, int offset, string expected)
	{
		if (value[offset] == expected[0] && value[offset + 1] == expected[1] && value[offset + 2] == expected[2])
		{
			return value[offset + 3] == expected[3];
		}
		return false;
	}

	private static SpeechException Error(string code, string message)
	{
		return new SpeechException(code, message);
	}
}
