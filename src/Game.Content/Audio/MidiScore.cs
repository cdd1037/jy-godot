namespace JinYong.Content.Audio;

public sealed record MidiMessage(int Tick, int Status, int Data1, int Data2);

/// <summary>Device-independent MIDI short messages on a fixed 120 Hz timeline.</summary>
public sealed record MidiScore
{
    public int TickRate { get; init; } = 120;
    public int EndTick { get; init; }
    public MidiMessage[] Messages { get; init; } = [];
    public int? LoopStartTick { get; init; }
    public int? LoopEndTick { get; init; }

    public void Validate()
    {
        if (TickRate != 120 || EndTick <= 0 || Messages is null || Messages.Length > 1_000_000)
            throw new InvalidDataException("A MIDI score requires a positive 120 Hz timeline and a bounded message list.");
        var previous = -1;
        foreach (var message in Messages)
        {
            if (message is null || message.Tick < previous || message.Tick < 0 || message.Tick > EndTick ||
                message.Status is < 0x80 or > 0xef || message.Data1 is < 0 or > 127 || message.Data2 is < 0 or > 127 ||
                (message.Status >> 4 is 0xc or 0xd) && message.Data2 != 0)
                throw new InvalidDataException("Invalid or unordered MIDI short message.");
            previous = message.Tick;
        }
        if (LoopStartTick.HasValue != LoopEndTick.HasValue ||
            LoopStartTick is { } start && (start < 0 || LoopEndTick <= start || LoopEndTick != EndTick))
            throw new InvalidDataException("An infinite MIDI loop requires a nonempty final timeline interval.");
    }
}
