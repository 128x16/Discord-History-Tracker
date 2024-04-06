using System.Collections.Immutable;

namespace DHT.Server.Data;

public readonly struct Poll {
	public string Question { get; internal init; }
	public ImmutableList<PollAnswer> Answers { get; internal init; }
	public bool MultiSelect { get; internal init; }
	public long ExpiryTimestamp { get; internal init; }
};
