using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Just_Lilith.Core.Llm;

public sealed class WorldBookRetriever
{
	private sealed record IndexedEntry(int Ordinal, WorldBookEntry Entry, string NormalizedTitle, string NormalizedContent, HashSet<string> TitleBigrams, HashSet<string> TitleTrigrams, HashSet<string> AllBigrams, HashSet<string> AllTrigrams, HashSet<string> AllContinuousGrams, Dictionary<string, int[]> TitleBigramPositions, Dictionary<string, int[]> ContentBigramPositions)
	{
		public static IndexedEntry Create(WorldBookEntry entry, int ordinal)
		{
			ArgumentNullException.ThrowIfNull(entry, "entry");
			string text = Normalize(entry.Title);
			string text2 = Normalize(entry.Content);
			HashSet<string> hashSet = Grams(text, 2);
			HashSet<string> hashSet2 = Grams(text, 3);
			HashSet<string> hashSet3 = new HashSet<string>(hashSet, StringComparer.Ordinal);
			hashSet3.UnionWith(Grams(text2, 2));
			HashSet<string> hashSet4 = new HashSet<string>(hashSet2, StringComparer.Ordinal);
			hashSet4.UnionWith(Grams(text2, 3));
			return new IndexedEntry(ordinal, entry, text, text2, hashSet, hashSet2, hashSet3, hashSet4, new HashSet<string>(hashSet3.Concat(hashSet4), StringComparer.Ordinal), BuildGramPositions(text), BuildGramPositions(text2));
		}

		private static Dictionary<string, int[]> BuildGramPositions(string value)
		{
			Dictionary<string, List<int>> dictionary = new Dictionary<string, List<int>>(StringComparer.Ordinal);
			for (int i = 0; i < value.Length - 1; i++)
			{
				string key = value.Substring(i, 2);
				if (!dictionary.TryGetValue(key, out var value2))
				{
					value2 = (dictionary[key] = new List<int>());
				}
				value2.Add(i);
			}
			return dictionary.ToDictionary<KeyValuePair<string, List<int>>, string, int[]>((KeyValuePair<string, List<int>> pair) => pair.Key, (KeyValuePair<string, List<int>> pair) => pair.Value.ToArray(), StringComparer.Ordinal);
		}
	}

	public const int MaximumMatches = 3;

	public const int MaximumDetailedCandidates = 64;

	private const int ReservedTitleCandidates = 32;

	private const int ReservedOverallCandidates = 32;

	public const int MaximumOrderedGap = 2;

	public const double MinimumScore = 20.0;

	public const double RelativeScoreFloor = 0.45;

	private readonly IndexedEntry[] entries;

	private readonly Dictionary<string, int[]> candidatesByGram;

	public WorldBookRetriever(IReadOnlyList<WorldBookEntry> source)
	{
		ArgumentNullException.ThrowIfNull(source, "source");
		entries = source.Select((WorldBookEntry entry, int index) => IndexedEntry.Create(entry, index)).ToArray();
		Dictionary<string, List<int>> dictionary = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		IndexedEntry[] array = entries;
		foreach (IndexedEntry indexedEntry in array)
		{
			foreach (string allContinuousGram in indexedEntry.AllContinuousGrams)
			{
				if (!dictionary.TryGetValue(allContinuousGram, out var value))
				{
					value = (dictionary[allContinuousGram] = new List<int>());
				}
				value.Add(indexedEntry.Ordinal);
			}
		}
		candidatesByGram = dictionary.ToDictionary<KeyValuePair<string, List<int>>, string, int[]>((KeyValuePair<string, List<int>> pair) => pair.Key, (KeyValuePair<string, List<int>> pair) => pair.Value.Distinct().ToArray(), StringComparer.Ordinal);
	}

	public IReadOnlyList<WorldBookMatch> Search(string input)
	{
		ArgumentNullException.ThrowIfNull(input, "input");
		string query = Normalize(input);
		if (query.Length < 2 || entries.Length == 0)
		{
			return Array.Empty<WorldBookMatch>();
		}
		HashSet<string> queryBigrams = Grams(query, 2);
		HashSet<string> queryTrigrams = Grams(query, 3);
		HashSet<int> hashSet = new HashSet<int>();
		foreach (string item2 in queryBigrams.Concat(queryTrigrams))
		{
			if (candidatesByGram.TryGetValue(item2, out int[] value))
			{
				int[] array = value;
				foreach (int item in array)
				{
					hashSet.Add(item);
				}
			}
		}
		if (hashSet.Count == 0)
		{
			return Array.Empty<WorldBookMatch>();
		}
		(int Index, int TitlePreliminary, int OverallPreliminary)[] source = hashSet.Select((int index) => (Index: index, TitlePreliminary: CountIntersection(queryBigrams, entries[index].TitleBigrams) + CountIntersection(queryTrigrams, entries[index].TitleTrigrams) * 2, OverallPreliminary: CountIntersection(queryBigrams, entries[index].AllBigrams) + CountIntersection(queryTrigrams, entries[index].AllTrigrams) * 2)).ToArray();
		HashSet<int> hashSet2 = new HashSet<int>();
		foreach (var item3 in (from tuple2 in source
			where tuple2.TitlePreliminary > 0
			orderby tuple2.TitlePreliminary descending, tuple2.OverallPreliminary descending, tuple2.Index
			select tuple2).Take(32))
		{
			hashSet2.Add(item3.Index);
		}
		foreach (var item4 in (from tuple2 in source
			orderby tuple2.OverallPreliminary descending, tuple2.TitlePreliminary descending, tuple2.Index
			select tuple2).Take(32))
		{
			hashSet2.Add(item4.Index);
		}
		if (hashSet2.Count < 64)
		{
			foreach (var item5 in from tuple2 in source
				orderby tuple2.OverallPreliminary descending, tuple2.TitlePreliminary descending, tuple2.Index
				select tuple2)
			{
				hashSet2.Add(item5.Index);
				if (hashSet2.Count == 64)
				{
					break;
				}
			}
		}
		(int, WorldBookMatch)[] array2 = (from index in hashSet2
			select (Index: index, Match: new WorldBookMatch(entries[index].Entry, Score(query, queryBigrams, queryTrigrams, entries[index]))) into tuple2
			where tuple2.Match.Score >= 20.0
			orderby tuple2.Match.Score descending, tuple2.Index
			select tuple2).ToArray();
		if (array2.Length == 0)
		{
			return Array.Empty<WorldBookMatch>();
		}
		double num = Math.Max(20.0, array2[0].Item2.Score * 0.45);
		List<WorldBookMatch> list = new List<WorldBookMatch>(3);
		HashSet<string> hashSet3 = new HashSet<string>(StringComparer.Ordinal);
		(int, WorldBookMatch)[] array3 = array2;
		for (int i = 0; i < array3.Length; i++)
		{
			(int, WorldBookMatch) tuple = array3[i];
			if (tuple.Item2.Score < num)
			{
				break;
			}
			if (hashSet3.Add(tuple.Item2.Entry.Content))
			{
				list.Add(tuple.Item2);
				if (list.Count == 3)
				{
					break;
				}
			}
		}
		return Array.AsReadOnly(list.ToArray());
	}

	private static double Score(string query, HashSet<string> queryBigrams, HashSet<string> queryTrigrams, IndexedEntry entry)
	{
		int val = Math.Min(8, LongestContinuous(query, entry.NormalizedTitle));
		int val2 = Math.Min(8, LongestOrderedGap(query, entry.TitleBigramPositions, entry.NormalizedTitle.Length, 2));
		int num = Math.Max(val, val2);
		int val3 = Math.Min(8, LongestContinuous(query, entry.NormalizedContent));
		int val4 = Math.Min(8, LongestOrderedGap(query, entry.ContentBigramPositions, entry.NormalizedContent.Length, 2));
		int num2 = Math.Max(val3, val4);
		int num3 = CountIntersection(queryBigrams, entry.AllBigrams);
		int num4 = CountIntersection(queryTrigrams, entry.AllTrigrams);
		return 5.0 * (double)num * (double)num + 1.25 * (double)num2 * (double)num2 + (double)num3 + (double)num4 * 2.0;
	}

	private static int LongestContinuous(string query, string target)
	{
		string text = ((query.Length <= target.Length) ? query : target);
		string text2 = ((query.Length <= target.Length) ? target : query);
		for (int num = Math.Min(8, text.Length); num >= 2; num--)
		{
			for (int i = 0; i <= text.Length - num; i++)
			{
				if (text2.AsSpan().IndexOf(text.AsSpan(i, num), StringComparison.Ordinal) >= 0)
				{
					return num;
				}
			}
		}
		return 0;
	}

	private static int LongestOrderedGap(string query, Dictionary<string, int[]> positionsByGram, int targetLength, int maxGap)
	{
		if (query.Length < 2 || targetLength < 2)
		{
			return 0;
		}
		int num = maxGap + 2;
		int width = targetLength - 1;
		int[][] array = (from _ in Enumerable.Range(0, num + 1)
			select new int[width]).ToArray();
		int num2 = 0;
		for (int num3 = 0; num3 < query.Length - 1; num3++)
		{
			int[] array2 = array[num3 % array.Length];
			Array.Clear(array2, 0, array2.Length);
			if (!positionsByGram.TryGetValue(query.Substring(num3, 2), out int[] value))
			{
				continue;
			}
			int[] array3 = value;
			foreach (int num5 in array3)
			{
				int num6 = 0;
				for (int num7 = 1; num7 <= num && num3 - num7 >= 0; num7++)
				{
					int[] array4 = array[(num3 - num7) % array.Length];
					for (int num8 = 1; num8 <= num && num5 - num8 >= 0; num8++)
					{
						if (num7 == 1 != (num8 == 1))
						{
							continue;
						}
						int num9 = array4[num5 - num8];
						if (num9 != 0)
						{
							num9 += ((num7 == 1) ? 1 : 2);
							if (num9 > num6)
							{
								num6 = num9;
							}
						}
					}
				}
				array2[num5] = ((num6 == 0) ? 2 : num6);
				if (array2[num5] > num2)
				{
					num2 = array2[num5];
					if (num2 >= 8)
					{
						return 8;
					}
				}
			}
		}
		return num2;
	}

	private static int CountIntersection(HashSet<string> left, HashSet<string> right)
	{
		if (left.Count > right.Count)
		{
			HashSet<string> hashSet = right;
			right = left;
			left = hashSet;
		}
		int num = 0;
		foreach (string item in left)
		{
			if (right.Contains(item))
			{
				num++;
			}
		}
		return num;
	}

	private static HashSet<string> Grams(string value, int size)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i <= value.Length - size; i++)
		{
			hashSet.Add(value.Substring(i, size));
		}
		return hashSet;
	}

	private static string Normalize(string value)
	{
		string text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		string text2 = text;
		foreach (char c in text2)
		{
			UnicodeCategory unicodeCategory = char.GetUnicodeCategory(c);
			if (((uint)(unicodeCategory - 11) > 4u && (uint)(unicodeCategory - 18) > 10u) || 1 == 0)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString().Replace("莉莉丝", "", StringComparison.Ordinal);
	}
}
