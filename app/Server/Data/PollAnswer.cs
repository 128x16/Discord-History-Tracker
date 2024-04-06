namespace DHT.Server.Data;

public readonly struct PollAnswer {
	public int Id { get; internal init; }
	public string Text { get; internal init; }
	public ulong? EmojiId { get; internal init; }
	public string? EmojiName { get; internal init; }
	public EmojiFlags EmojiFlags { get; internal init; }
};
