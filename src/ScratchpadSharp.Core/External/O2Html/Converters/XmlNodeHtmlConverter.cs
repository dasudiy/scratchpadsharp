using System.Text;
using System.Xml;
using ScratchpadSharp.Core.External.O2Html.Common;
using ScratchpadSharp.Core.External.O2Html.Dom;

namespace ScratchpadSharp.Core.External.O2Html.Converters;

public class XmlNodeHtmlConverter : DotNetTypeWithStringRepresentationHtmlConverter
{
    private static readonly Lazy<XmlWriterSettings> _xmlWriterSettings = new(() => new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 });

    public override bool CanConvert(Type type)
    {
        return typeof(XmlNode).IsAssignableFrom(type);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not XmlNode xmlNode)
            throw new HtmlSerializationException($"The {nameof(XmlNodeHtmlConverter)} can only convert objects of type {nameof(XmlNode)}");

        string xml;

        using (var stringWriter = new StringWriter())
        using (var xmlTextWriter = XmlWriter.Create(stringWriter, _xmlWriterSettings.Value))
        {
            xmlNode.WriteTo(xmlTextWriter);
            xmlTextWriter.Flush();
            xml = stringWriter.ToString();
        }

        var pre = new Element("pre");
        pre.AddAndGetElement("code")
            .SetAttribute("language", "xml")
            .AddText(Util.EscapeAngleBracketsForHtml(xml));

        return pre;
    }
}
