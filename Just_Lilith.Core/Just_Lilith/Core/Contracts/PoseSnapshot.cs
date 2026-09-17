using System;

namespace Just_Lilith.Core.Contracts;

public sealed record PoseSnapshot(PoseState State, DateTimeOffset CapturedAt);
