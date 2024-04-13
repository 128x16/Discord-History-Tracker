using System;

namespace DHT.Server.Data;

[Flags]
public enum PollFlags : ushort {
    None = 0,
    MultiSelect = 0b1,
    Expired = 0b10
}
