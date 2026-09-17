using System;

namespace Just_Lilith.Core.Contracts;

public sealed record DisplayReply(RequestIdentity Identity, Guid MessageId, string Text);
