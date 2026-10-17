namespace Ryujinx.Graphics.Vulkan
{
    static class Constants
    {
        public const int MaxVertexAttributes = 32;
        public const int MaxVertexBuffers = 32;
        public const int MaxTransformFeedbackBuffers = 4;
        public const int MaxRenderTargets = 8;
        public const int MaxViewports = 16;
        public const int MaxShaderStages = 5;
        public const int MaxUniformBuffersPerStage = 18;
        public const int MaxStorageBuffersPerStage = 16;
        public const int MaxTexturesPerStage = 64;
        public const int MaxImagesPerStage = 16;
        // [Nextendo] + 2 : le tampon de support et celui du vertex-en-compute prennent une liaison apres
        // les 18 blocs de chaque etage ; certains shaders de Smash Ultimate (tessellation, bloc 17)
        // depassaient le tableau (IndexOutOfRange dans DescriptorSetUpdater.SetUniformBuffers).
        public const int MaxUniformBufferBindings = MaxUniformBuffersPerStage * MaxShaderStages + 2;
        public const int MaxStorageBufferBindings = MaxStorageBuffersPerStage * MaxShaderStages;
        public const int MaxTextureBindings = MaxTexturesPerStage * MaxShaderStages;
        public const int MaxImageBindings = MaxImagesPerStage * MaxShaderStages;
        public const int MaxPushDescriptorBinding = 64;

        public const ulong SparseBufferAlignment = 0x10000;
    }
}
