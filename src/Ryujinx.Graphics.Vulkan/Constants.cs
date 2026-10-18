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
        // [Nextendo] Liaisons reservees (ResourceReservations) : elles prennent les premieres liaisons
        // et decalent tous les blocs de tous les etages. Tampons de constantes : 0 (support) et 1
        // (vertex-en-compute). Tampons de stockage : 4 (retour de transformation emule) et 3
        // (vertex-en-compute). Textures : 2 + 32 (vertex-en-compute). Sans elles, le dernier bloc de
        // la tessellation d'evaluation (indice d'etage 4) sortait des tableaux de DescriptorSetUpdater :
        // bloc de constantes 17 de certains shaders de Smash Ultimate = liaison 1 + 4 * 18 + 17 = 90
        // pour 90 cases (IndexOutOfRange dans SetUniformBuffers) ; derniere liaison possible
        // = 2 + 4 * 18 + 17 = 91.
        public const int ReservedUniformBufferBindings = 2;
        public const int ReservedStorageBufferBindings = 4 + 3;
        public const int ReservedTextureBindings = 2 + 32;
        public const int MaxUniformBufferBindings = MaxUniformBuffersPerStage * MaxShaderStages + ReservedUniformBufferBindings;
        public const int MaxStorageBufferBindings = MaxStorageBuffersPerStage * MaxShaderStages + ReservedStorageBufferBindings;
        public const int MaxTextureBindings = MaxTexturesPerStage * MaxShaderStages;
        public const int MaxImageBindings = MaxImagesPerStage * MaxShaderStages;
        public const int MaxPushDescriptorBinding = 64;

        public const ulong SparseBufferAlignment = 0x10000;
    }
}
