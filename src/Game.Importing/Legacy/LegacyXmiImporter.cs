using System.Buffers.Binary;
using System.Text;
using JinYong.Content.Audio;

namespace JinYong.Importing.Legacy;

/// <summary>
/// Converts one XMIDI sequence to a fixed-rate score, without a MIDI device or
/// instrument synthesis. Format reference (not copied implementation):
/// https://github.com/scummvm/scummvm/blob/master/audio/midiparser_xmidi.cpp
/// XMIDI uses 60 PPQN at a constant 500000 us/qn: 120 physical ticks/second;
/// retained SMF tempo metadata does not rescale that physical timeline.
/// </summary>
public static class LegacyXmiImporter
{
    private const int MaximumEvents = 1_000_000;
    private enum EventKind { Short, Note, Timing, For, Next, End }
    private sealed record SourceEvent(int Delta, EventKind Kind, int Status = 0, int Data1 = 0, int Data2 = 0, int Duration = 0);
    private sealed record ScheduledMessage(MidiMessage Message, int Priority, int Order);
    private sealed class Loop(int sourceIndex, int repeat, int tick)
    {
        public int SourceIndex { get; } = sourceIndex;
        public int Remaining { get; set; } = repeat;
        public int StartTick { get; } = tick;
    }
    private sealed class Chunks
    {
        public int? TrackCount;
        public List<(int Offset, int Length)> Events { get; } = [];
    }

    public static MidiScore Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var chunks = new Chunks();
            ScanChunks(bytes, 0, bytes.Length, 0, null, chunks);
            if (chunks.Events.Count != 1 || chunks.TrackCount is { } tracks && tracks != 1)
                throw new NotSupportedException("Exactly one XMIDI sequence is supported per music file.");
            var (offset, length) = chunks.Events[0];
            return Normalize(ReadEvents(bytes.Slice(offset, length)));
        }
        catch (OverflowException error)
        { throw new InvalidDataException("XMIDI lengths or timeline exceed the supported range.", error); }
    }

    private static void ScanChunks(ReadOnlySpan<byte> bytes, int start, int end, int depth, string? parent, Chunks result)
    {
        if (depth > 8) throw new InvalidDataException("XMIDI container nesting is too deep.");
        for (var cursor = start; cursor < end;)
        {
            if (end - cursor < 8) throw new InvalidDataException("Truncated XMIDI chunk header.");
            var name = Encoding.ASCII.GetString(bytes.Slice(cursor, 4));
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(cursor + 4, 4)));
            var body = checked(cursor + 8); var stop = checked(body + length);
            if (stop > end) throw new InvalidDataException($"XMIDI {name} chunk exceeds its container.");
            var data = bytes.Slice(body, length);
            if (name is "FORM" or "CAT ")
            {
                if (length < 4) throw new InvalidDataException("XMIDI container has no type.");
                var type = Encoding.ASCII.GetString(data[..4]);
                if (type is not ("XDIR" or "XMID")) throw new NotSupportedException($"Unsupported XMIDI container {type}.");
                ScanChunks(bytes, body + 4, stop, depth + 1, type, result);
            }
            else if (name == "INFO" && parent == "XDIR")
            {
                if (length != 2 || result.TrackCount.HasValue) throw new InvalidDataException("Invalid or duplicate XMIDI INFO.");
                result.TrackCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
            }
            else if (name == "EVNT" && parent == "XMID") result.Events.Add((body, length));
            else if (name == "TIMB" && parent == "XMID")
            {
                // This is the advisory patch/bank preload list, not embedded
                // instruments. The host deliberately uses its GM device timbres.
                if (length < 2 || length != 2 + 2 * BinaryPrimitives.ReadUInt16LittleEndian(data) ||
                    data[2..].IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0)
                    throw new InvalidDataException("Invalid XMIDI TIMB preload list.");
            }
            else throw new NotSupportedException($"Unsupported XMIDI chunk {name}; sequence branches are not inferred.");
            var padded = checked(stop + (length & 1));
            if (padded > end) throw new InvalidDataException("XMIDI chunk is missing its IFF padding byte.");
            cursor = padded;
        }
    }

    private static List<SourceEvent> ReadEvents(ReadOnlySpan<byte> bytes)
    {
        var events = new List<SourceEvent>();
        var cursor = 0; var ended = false;
        while (cursor < bytes.Length)
        {
            if (events.Count >= MaximumEvents) throw new InvalidDataException("XMIDI event count exceeds its safety budget.");
            var delta = 0;
            // XMIDI deltas are a sum of low bytes, unlike MIDI duration VLQs.
            while (cursor < bytes.Length && bytes[cursor] < 0x80) delta = checked(delta + bytes[cursor++]);
            if (cursor >= bytes.Length) throw new InvalidDataException("XMIDI delay has no following event.");
            var status = bytes[cursor++];
            if (status < 0xf0)
            {
                var first = DataByte(bytes, ref cursor);
                var second = status >> 4 is 0xc or 0xd ? 0 : DataByte(bytes, ref cursor);
                if (status >> 4 == 0x9)
                {
                    var duration = Vlq(bytes, ref cursor);
                    // AIL gives zero-duration notes a short lifetime; preserve
                    // termination rather than create an indefinitely held note.
                    events.Add(second == 0 ? new(delta, EventKind.Short, 0x80 | (status & 15), first, 0) :
                        new(delta, EventKind.Note, status, first, second, Math.Max(1, duration)));
                }
                else if (status >> 4 == 0xb && first is >= 110 and <= 120)
                {
                    if (first == 116) events.Add(new(delta, EventKind.For, status, first, second));
                    else if (first == 117) events.Add(new(delta, EventKind.Next, status, first, second));
                    else throw new NotSupportedException($"XMIDI controller {first} requires unsupported Miles routing, callbacks or branching.");
                }
                else events.Add(new(delta, EventKind.Short, status, first, second));
            }
            else if (status == 0xff)
            {
                var type = Take(bytes, ref cursor);
                var size = Vlq(bytes, ref cursor);
                if (size > bytes.Length - cursor) throw new InvalidDataException("Truncated XMIDI metadata.");
                var data = bytes.Slice(cursor, size); cursor += size;
                ValidateMeta(type, data);
                events.Add(new(delta, type == 0x2f ? EventKind.End : EventKind.Timing));
                if (type == 0x2f) { ended = true; break; }
            }
            else throw new NotSupportedException($"XMIDI system event 0x{status:X2} cannot be sent as a MIDI short message.");
        }
        if (!ended || cursor != bytes.Length) throw new InvalidDataException("XMIDI EVNT must end at its end-of-track event.");
        return events;
    }

    private static void ValidateMeta(int type, ReadOnlySpan<byte> data)
    {
        switch (type)
        {
            case >= 1 and <= 9: return; // Descriptive text, not sound/timing commands.
            case 0 when data.Length == 2: return; // Sequence number.
            case 0x20 when data.Length == 1 && data[0] < 16: return; // Metadata channel prefix.
            case 0x21 when data.Length == 1 && data[0] == 0: return; // Single output port.
            case 0x2f when data.IsEmpty: return;
            case 0x51 when data.Length == 3 && (data[0] | data[1] | data[2]) != 0: return; // Fixed 120 Hz XMIDI clock.
            case 0x58 when data.Length == 4: return; // Time signature is notation only.
            case 0x59 when data.Length == 2: return; // Key signature is notation only.
            default: throw new NotSupportedException($"Unsupported or malformed XMIDI metadata 0x{type:X2}.");
        }
    }

    private static MidiScore Normalize(IReadOnlyList<SourceEvent> source)
    {
        var messages = new List<ScheduledMessage>();
        var loops = new Stack<Loop>();
        var tick = 0; var endTick = 0; var order = 0; var executed = 0;
        int? loopStart = null, loopEnd = null;
        void Add(int at, int status, int first, int second, int priority)
        {
            if (messages.Count >= MaximumEvents) throw new InvalidDataException("Expanded XMIDI exceeds its MIDI message budget.");
            messages.Add(new(new(at, status, first, second), priority, order++));
            endTick = Math.Max(endTick, at);
        }
        for (var index = 0; index < source.Count; index++)
        {
            if (++executed > MaximumEvents) throw new InvalidDataException("Expanded XMIDI exceeds its event execution budget.");
            var entry = source[index]; tick = checked(tick + entry.Delta); endTick = Math.Max(endTick, tick);
            switch (entry.Kind)
            {
                case EventKind.Short: Add(tick, entry.Status, entry.Data1, entry.Data2, 1); break;
                case EventKind.Note:
                    Add(tick, entry.Status, entry.Data1, entry.Data2, 1);
                    Add(checked(tick + entry.Duration), 0x80 | (entry.Status & 15), entry.Data1, 0, 0); break;
                case EventKind.Timing: break;
                case EventKind.For:
                    if (loops.Count >= 4) throw new NotSupportedException("XMIDI loop nesting exceeds four levels.");
                    loops.Push(new(index + 1, entry.Data2, tick)); break;
                case EventKind.Next:
                    if (!loops.TryPeek(out var loop)) throw new InvalidDataException("XMIDI NEXT/BREAK has no matching FOR.");
                    if (entry.Data2 < 64 || loop.Remaining == 1) loops.Pop();
                    else if (loop.Remaining > 1) { loop.Remaining--; index = loop.SourceIndex - 1; }
                    else
                    {
                        if (loops.Count != 1 || tick <= loop.StartTick || endTick > tick)
                            throw new NotSupportedException("An infinite XMIDI loop must be outermost, nonempty and contain all its note endings.");
                        loopStart = loop.StartTick; loopEnd = tick;
                        loops.Pop(); index = source.Count; // Following events are unreachable in this infinite loop.
                    }
                    break;
                case EventKind.End:
                    if (loops.Count > 0) throw new InvalidDataException("XMIDI has an unterminated FOR loop.");
                    break;
                default: throw new InvalidDataException("Unknown normalized XMIDI source event.");
            }
        }
        var result = new MidiScore
        {
            EndTick = Math.Max(1, endTick), LoopStartTick = loopStart, LoopEndTick = loopEnd,
            Messages = messages.OrderBy(message => message.Message.Tick).ThenBy(message => message.Priority)
                .ThenBy(message => message.Order).Select(message => message.Message).ToArray()
        };
        result.Validate();
        return result;
    }

    private static int Take(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        if (cursor >= bytes.Length) throw new InvalidDataException("Truncated XMIDI event.");
        return bytes[cursor++];
    }
    private static int DataByte(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        var value = Take(bytes, ref cursor);
        if (value >= 128) throw new InvalidDataException("XMIDI event contains an invalid data byte.");
        return value;
    }
    private static int Vlq(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        var result = 0;
        for (var count = 0; count < 4; count++)
        {
            var value = Take(bytes, ref cursor);
            result = checked((result << 7) | (value & 127));
            if ((value & 128) == 0) return result;
        }
        throw new InvalidDataException("XMIDI VLQ exceeds four bytes.");
    }
}
