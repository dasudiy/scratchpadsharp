using ScratchpadSharp.Core.External.O2Html.Dom;

namespace ScratchpadSharp.Core.External.O2Html.Converters;

public class DotNetTypeWithStringRepresentationHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return HtmlSerializer.IsDotNetTypeWithStringRepresentation(type);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        return TextNode.EscapedText(obj!.ToString()!);
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td")
            .AddClass(htmlSerializer.SerializerOptions.CssClasses.PropertyValue)
            .AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }
}
