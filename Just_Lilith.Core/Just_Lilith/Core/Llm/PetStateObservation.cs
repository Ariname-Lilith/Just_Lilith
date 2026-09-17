using System;

namespace Just_Lilith.Core.Llm;

public sealed record PetStateObservation(DateTimeOffset CapturedAt, string? CurrentStateTypeName = null, string? ActionTypeName = null, string? DrowsinessName = null, string? SleepKindName = null, string? ControlModeName = null, string? ClothingName = null, string? ExpressionAccessoryName = null, bool? IsIdle = null, bool? IsSleep = null, bool? IsLieDown = null, bool? IsSit = null, bool? IsSofaSit = null, bool? IsInteracting = null, bool? IsDrag = null, bool? IsGround = null, bool? IsFalling = null, bool? IsWalkLeft = null, bool? IsWalkRight = null, bool? IsStateMachineLocked = null, bool? IsActionAnimationPlaying = null, bool? IsAnyActionAnimationPlaying = null, bool? IsYawnAnimationPlaying = null);
