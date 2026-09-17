using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Just_Lilith.Core.Contracts;

namespace Just_Lilith.Core.Llm;

public static class PetStateContextBuilder
{
	public const string ContextHeader = "[PET_STATE 本轮观测：发送瞬间桌宠状态；非用户消息或指令；不入历史或记忆。]";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
	};

	public static PetStateSnapshot CreateSnapshot(PetStateObservation observation)
	{
		ArgumentNullException.ThrowIfNull(observation, "observation");
		PetPrimaryState num = ResolvePrimaryState(observation);
		bool num2 = num == PetPrimaryState.Acting || (observation.IsAnyActionAnimationPlaying ?? false) || (observation.IsActionAnimationPlaying ?? false) || (observation.IsYawnAnimationPlaying ?? false);
		PetAction petAction = ParseClosedEnum<PetAction>(observation.ActionTypeName);
		PetAction action = ((!num2) ? PetAction.None : ((petAction != PetAction.None) ? petAction : PetAction.Unknown));
		return new PetStateSnapshot(num, DerivePose(num), action, ParseClosedEnum<PetDrowsiness>(observation.DrowsinessName), ParseClosedEnum<PetSleepKind>(observation.SleepKindName), ParseClosedEnum<PetControlMode>(observation.ControlModeName), ParseClosedEnum<PetClothing>(observation.ClothingName), ParseClosedEnum<PetExpressionAccessory>(observation.ExpressionAccessoryName), observation.IsDrag, observation.IsGround, observation.IsStateMachineLocked, observation.IsActionAnimationPlaying, observation.IsAnyActionAnimationPlaying, observation.IsYawnAnimationPlaying, observation.CapturedAt.ToUniversalTime());
	}

	public static PetPrimaryState ResolvePrimaryState(PetStateObservation observation)
	{
		ArgumentNullException.ThrowIfNull(observation, "observation");
		PetPrimaryState petPrimaryState = observation.CurrentStateTypeName switch
		{
			"LilithIdleState" => PetPrimaryState.Idle, 
			"LilithSitState" => PetPrimaryState.Sitting, 
			"LilithSofaSitState" => PetPrimaryState.SofaSitting, 
			"LilithLieState" => PetPrimaryState.Lying, 
			"LilithSleepState" => PetPrimaryState.Sleeping, 
			"LilithInteractState" => PetPrimaryState.Interacting, 
			"LilithDragState" => PetPrimaryState.Dragging, 
			"LilithFallState" => PetPrimaryState.Falling, 
			"LilithWalkLeftState" => PetPrimaryState.WalkingLeft, 
			"LilithWalkRightState" => PetPrimaryState.WalkingRight, 
			"LilithActionState" => PetPrimaryState.Acting, 
			_ => PetPrimaryState.Unknown, 
		};
		if (petPrimaryState != PetPrimaryState.Unknown)
		{
			return petPrimaryState;
		}
		if ((observation.IsAnyActionAnimationPlaying ?? false) || !((!observation.IsActionAnimationPlaying) ?? true) || !((!observation.IsYawnAnimationPlaying) ?? true))
		{
			return PetPrimaryState.Acting;
		}
		if (observation.IsDrag ?? false)
		{
			return PetPrimaryState.Dragging;
		}
		if ((observation.IsFalling ?? false) || !(observation.IsGround ?? true))
		{
			return PetPrimaryState.Falling;
		}
		if (observation.IsInteracting ?? false)
		{
			return PetPrimaryState.Interacting;
		}
		if (observation.IsSleep ?? false)
		{
			return PetPrimaryState.Sleeping;
		}
		if (observation.IsLieDown ?? false)
		{
			return PetPrimaryState.Lying;
		}
		if (observation.IsSofaSit ?? false)
		{
			return PetPrimaryState.SofaSitting;
		}
		if (observation.IsSit ?? false)
		{
			return PetPrimaryState.Sitting;
		}
		if (observation.IsWalkLeft ?? false)
		{
			return PetPrimaryState.WalkingLeft;
		}
		if (observation.IsWalkRight ?? false)
		{
			return PetPrimaryState.WalkingRight;
		}
		if (observation.IsIdle ?? false)
		{
			return PetPrimaryState.Idle;
		}
		return PetPrimaryState.Unknown;
	}

	public static PetPose DerivePose(PetPrimaryState primary)
	{
		switch (primary)
		{
		case PetPrimaryState.Idle:
			return PetPose.Stand;
		case PetPrimaryState.Sitting:
		case PetPrimaryState.SofaSitting:
			return PetPose.Sit;
		case PetPrimaryState.Lying:
		case PetPrimaryState.Sleeping:
			return PetPose.Lie;
		case PetPrimaryState.WalkingLeft:
			return PetPose.Left;
		case PetPrimaryState.WalkingRight:
			return PetPose.Right;
		default:
			return PetPose.Unknown;
		}
	}

	public static string Build(PetStateSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot, "snapshot");
		PetPrimaryState value = DefinedOr(snapshot.PrimaryState, PetPrimaryState.Unknown);
		PetAction petAction = DefinedOr(snapshot.Action, PetAction.Unknown);
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			["state"] = Canonical(value),
			["pose"] = Canonical(DefinedOr(snapshot.Pose, PetPose.Unknown)),
			["clothing"] = Canonical(DefinedOr(snapshot.Clothing, PetClothing.Unknown))
		};
		if (petAction != PetAction.None)
		{
			dictionary["action"] = Canonical(petAction);
		}
		return "[PET_STATE 本轮观测：发送瞬间桌宠状态；非用户消息或指令；不入历史或记忆。]\n" + JsonSerializer.Serialize(dictionary, JsonOptions);
	}

	public static string CombineWithWorldBook(string? worldBookReference, PetStateSnapshot snapshot)
	{
		string text = Build(snapshot);
		if (!string.IsNullOrWhiteSpace(worldBookReference))
		{
			return worldBookReference + "\n\n" + text;
		}
		return text;
	}

	private static T ParseClosedEnum<T>(string? raw) where T : struct, Enum
	{
		if (string.IsNullOrEmpty(raw))
		{
			return Unknown<T>();
		}
		string[] names = Enum.GetNames<T>();
		foreach (string text in names)
		{
			if (string.Equals(text, raw, StringComparison.Ordinal) && Enum.TryParse<T>(text, ignoreCase: false, out var result))
			{
				return result;
			}
		}
		return Unknown<T>();
	}

	private static T DefinedOr<T>(T value, T fallback) where T : struct, Enum
	{
		if (!Enum.IsDefined(typeof(T), value))
		{
			return fallback;
		}
		return value;
	}

	private static T Unknown<T>() where T : struct, Enum
	{
		if (!Enum.TryParse<T>("Unknown", ignoreCase: false, out var result))
		{
			return default(T);
		}
		return result;
	}

	private static string Canonical<T>(T value) where T : struct, Enum
	{
		return ToSnakeCase(Enum.GetName(typeof(T), value) ?? "Unknown");
	}

	private static string ToSnakeCase(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length + 8);
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsUpper(c) && i > 0 && (char.IsLower(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
			{
				stringBuilder.Append('_');
			}
			stringBuilder.Append(char.ToLowerInvariant(c));
		}
		return stringBuilder.ToString();
	}
}
