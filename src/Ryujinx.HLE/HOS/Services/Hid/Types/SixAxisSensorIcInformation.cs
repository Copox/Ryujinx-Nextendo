using Ryujinx.Common.Memory;
using System;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Hid.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0xC8)]
    struct SixAxisSensorIcInformation
    {
        public float AngularRateRange;
        public Array6<float> AngularRateZeroOffsetRange;
        public Array9<float> AngularRateSensitivityMin;
        public Array9<float> AngularRateSensitivityMax;
        public float AccelerationRange;
        public Array6<float> AccelerationZeroOffsetRange;
        public Array9<float> AccelerationSensitivityMin;
        public Array9<float> AccelerationSensitivityMax;

        public static SixAxisSensorIcInformation Create()
        {
            SixAxisSensorIcInformation information = new()
            {
                AngularRateRange = 2000.0f,
                AccelerationRange = 8.0f,
            };

            Set(information.AngularRateZeroOffsetRange.AsSpan(), [-10.0f, -10.0f, -10.0f, 10.0f, 10.0f, 10.0f]);
            Set(information.AngularRateSensitivityMin.AsSpan(), [0.95f, -0.003f, -0.003f, -0.003f, 0.95f, -0.003f, -0.003f, -0.003f, 0.95f]);
            Set(information.AngularRateSensitivityMax.AsSpan(), [1.05f, 0.003f, 0.003f, 0.003f, 1.05f, 0.003f, 0.003f, 0.003f, 1.05f]);
            Set(information.AccelerationZeroOffsetRange.AsSpan(), [-0.0612f, -0.0612f, -0.0612f, 0.0612f, 0.0612f, 0.0612f]);
            Set(information.AccelerationSensitivityMin.AsSpan(), [0.95f, -0.016f, -0.016f, -0.016f, 0.95f, -0.016f, -0.016f, -0.016f, 0.95f]);
            Set(information.AccelerationSensitivityMax.AsSpan(), [1.05f, 0.016f, 0.016f, 0.016f, 1.05f, 0.016f, 0.016f, 0.016f, 1.05f]);

            return information;
        }

        private static void Set(Span<float> destination, ReadOnlySpan<float> values)
        {
            values.CopyTo(destination);
        }
    }
}
