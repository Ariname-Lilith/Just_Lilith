using System;

namespace Just_Lilith.Core.Llm;

/// <summary>
/// 气泡节奏计划：<paramref name="Delay"/> 是回复入队到开始显示的等待，
/// <paramref name="VisibleDuration"/> 是打字机显示整段文字所需时间加读完后停留时间，
/// <paramref name="HoldDuration"/> 只包含“文字全部显示完之后”的停留时间。
/// </summary>
public readonly record struct BubblePresentationSchedule(TimeSpan Delay, TimeSpan VisibleDuration, TimeSpan HoldDuration);
