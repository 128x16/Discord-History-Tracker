using System.Collections.Immutable;

namespace DHT.Server.Data;

public readonly struct Poll {
	public string Question { get; internal init; }
	public ImmutableList<PollAnswer> Answers { get; internal init; }
	public PollFlags Flags { get; internal init; }
	public long ExpiryTimestamp { get; internal init; }
};
