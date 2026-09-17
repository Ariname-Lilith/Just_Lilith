using System;

namespace Just_Lilith.Core.Contracts;

public sealed record PetStateSnapshot(PetPrimaryState PrimaryState, PetPose Pose, PetAction Action, PetDrowsiness Drowsiness, PetSleepKind SleepKind, PetControlMode ControlMode, PetClothing Clothing, PetExpressionAccessory ExpressionAccessory, bool? Dragging, bool? Grounded, bool? StateMachineLocked, bool? ActionAnimationPlaying, bool? AnyActionAnimationPlaying, bool? YawnAnimationPlaying, DateTimeOffset CapturedAt)
{
	public PoseSnapshot ToLegacyPoseSnapshot()
	{
		PetPrimaryState primaryState = PrimaryState;
		PoseState state;
		switch (primaryState)
		{
		case PetPrimaryState.Sleeping:
			state = PoseState.Sleeping;
			break;
		case PetPrimaryState.Interacting:
		case PetPrimaryState.Dragging:
			state = PoseState.Interacting;
			break;
		default:
			if ((!YawnAnimationPlaying) ?? true)
			{
				switch (primaryState)
				{
				case PetPrimaryState.Lying:
					state = PoseState.Lying;
					break;
				case PetPrimaryState.Sitting:
				case PetPrimaryState.SofaSitting:
					state = PoseState.Sitting;
					break;
				case PetPrimaryState.Idle:
					state = PoseState.Idle;
					break;
				default:
					state = PoseState.Unknown;
					break;
				}
			}
			else
			{
				state = PoseState.Yawning;
			}
			break;
		}
		return new PoseSnapshot(state, CapturedAt);
	}
}
