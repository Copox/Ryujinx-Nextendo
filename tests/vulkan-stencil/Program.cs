using System.Reflection;
using Ryujinx.Graphics.Vulkan;
using Silk.NET.Vulkan;
using GalFormat = Ryujinx.Graphics.GAL.Format;
using VkFormat = Silk.NET.Vulkan.Format;

const FormatFeatureFlags Required = FormatFeatureFlags.SampledImageBit |
    FormatFeatureFlags.TransferSrcBit | FormatFeatureFlags.TransferDstBit |
    FormatFeatureFlags.DepthStencilAttachmentBit;

FormatCapabilities Create(params (GalFormat Format, FormatFeatureFlags Features)[] formats)
{
    var capabilities = new FormatCapabilities(null, default);
    var table = (FormatFeatureFlags[])typeof(FormatCapabilities)
        .GetField("_optimalTable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capabilities)!;
    // Nonzero unsupported entries avoid querying a physical device in these capability matrix tests.
    Array.Fill(table, FormatFeatureFlags.VertexBufferBit);
    foreach (var (format, features) in formats)
    {
        table[(int)format] = features;
    }
    return capabilities;
}

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS {name}");
    checks++;
}

Check(Create((GalFormat.S8Uint, Required)).ConvertToVkFormat(GalFormat.S8Uint, true) == VkFormat.S8Uint,
    "Native stencil format remains unchanged");
Check(Create((GalFormat.D24UnormS8Uint, Required), (GalFormat.D32FloatS8Uint, Required))
    .ConvertToVkFormat(GalFormat.S8Uint, true) == VkFormat.D24UnormS8Uint,
    "Unsupported stencil uses supported D24S8");
Check(Create((GalFormat.D32FloatS8Uint, Required)).ConvertToVkFormat(GalFormat.S8Uint, true) == VkFormat.D32SfloatS8Uint,
    "D32S8 is selected when D24S8 is unavailable");
foreach (var missing in new[] { FormatFeatureFlags.SampledImageBit, FormatFeatureFlags.TransferSrcBit,
    FormatFeatureFlags.TransferDstBit, FormatFeatureFlags.DepthStencilAttachmentBit })
{
    Check(Create((GalFormat.D24UnormS8Uint, Required & ~missing), (GalFormat.D32FloatS8Uint, Required))
        .ConvertToVkFormat(GalFormat.S8Uint, true) == VkFormat.D32SfloatS8Uint,
        $"D24S8 without {missing} is rejected");
}
bool rejected = false;
try { Create().ConvertToVkFormat(GalFormat.S8Uint, true); }
catch (NotSupportedException) { rejected = true; }
Check(rejected, "No supported fallback fails before vkCreateImage");
Check(!FormatCapabilities.IsD24S8(GalFormat.S8Uint), "Stencil transfers do not enter packed depth conversion");
Check(Create((GalFormat.D24UnormS8Uint, Required)).ConvertToVkFormat(GalFormat.D24UnormS8Uint, true) == VkFormat.D24UnormS8Uint,
    "Supported depth/stencil format remains unchanged");

var pipelineType = typeof(VulkanRenderer).Assembly.GetType("Ryujinx.Graphics.Vulkan.PipelineState", true)!;
var pipeline = Activator.CreateInstance(pipelineType)!;
var stencilOnly = pipelineType.GetProperty("StencilOnlyAttachment")!;
var stateField = pipelineType.GetField("Internal")!;
var uid = stateField.GetValue(pipeline)!;
var id8 = uid.GetType().GetField("Id8")!;
id8.SetValue(uid, 0x155UL);
stateField.SetValue(pipeline, uid);
var originalUid = stateField.GetValue(pipeline)!;
var equalsCacheKey = originalUid.GetType().GetMethod("Equals", [originalUid.GetType().MakeByRefType()])!;
stencilOnly.SetValue(pipeline, true);
var stencilUid = stateField.GetValue(pipeline)!;
Check((ulong)id8.GetValue(stencilUid)! == 0x355UL, "Stencil-only state preserves neighboring pipeline flags");
Check(!(bool)equalsCacheKey.Invoke(originalUid, [stencilUid])!, "Stencil-only and depth/stencil pipelines have distinct cache keys");
stencilOnly.SetValue(pipeline, false);
Check((bool)equalsCacheKey.Invoke(originalUid, [stateField.GetValue(pipeline)])!, "Returning to depth/stencil restores the pipeline cache key");
Console.WriteLine($"{checks} checks passed.");
