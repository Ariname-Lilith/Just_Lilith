using System;
using Just_Lilith.Core.Contracts;

namespace Just_Lilith.Unity.Ui;

internal sealed class SpeechUiCallbacks
{
	public Action<bool>? SelectServiceEnabled { get; init; }

	public Action<SpeechLanguage>? SelectLanguage { get; init; }

	public Action<string?>? SelectReference { get; init; }
}
