namespace JinYong.Core.Maps;

public sealed partial class SceneRuntimeState
{
    /// <summary>
    /// OpenLegend SceneSession::advance_event_pictures: words5/6/7/8 are
    /// first/end/displayed/delay. Visit map cells in x-major order, including aliases.
    /// Explicit script animation sets first=end=displayed and cannot be advanced here.
    /// </summary>
    internal void AdvanceEventPictures(int counter)
    {
        for (var x = 0; x < SceneDefinition.Extent; x++)
        for (var y = 0; y < SceneDefinition.Extent; y++)
        {
            var index = At(MapLayer.Event, new(x, y));
            if (index < 0) continue;
            var point = _events[index];
            var first = point[5];
            if (first <= 0) continue;
            var end = point[6];
            var displayed = (int)point[7];
            if (displayed >= end) displayed = first;
            if (displayed > first && counter % 4 == 0 && displayed < end) displayed = unchecked((short)(displayed + 2));
            if (point[8] <= counter % 100 && displayed == first && displayed < end)
                displayed = unchecked((short)(displayed + 2));
            if (displayed != point[7]) SetEventField(index, 7, displayed);
        }
    }
}
