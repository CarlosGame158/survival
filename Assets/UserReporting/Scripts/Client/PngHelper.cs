﻿using System;

namespace Unity.Cloud.UserReporting
{
    /// <summary>
    ///     Provides static methods for helping with PNG images.
    /// </summary>
    public static class PngHelper
    {
        #region Static Methods

        /// <summary>
        ///     Gets a PNG image's height from base 64 encoded data.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>The height.</returns>
        public static int GetPngHeightFromBase64Data(string data)
        {
            // Preconditions
            if (data == null || data.Length < 32) return 0;

            // Implementation
            var bytes = Convert.FromBase64String(data.Substring(0, 32));
            var heightBytes = Slice(bytes, 20, 24);
            Array.Reverse(heightBytes);
            var height = BitConverter.ToInt32(heightBytes, 0);
            return height;
        }

        /// <summary>
        ///     Gets a PNG image's width from base 64 encoded data.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>The width.</returns>
        public static int GetPngWidthFromBase64Data(string data)
        {
            // Preconditions
            if (data == null || data.Length < 32) return 0;

            // Implementation
            var bytes = Convert.FromBase64String(data.Substring(0, 32));
            var widthBytes = Slice(bytes, 16, 20);
            Array.Reverse(widthBytes);
            var width = BitConverter.ToInt32(widthBytes, 0);
            return width;
        }

        /// <summary>
        ///     Slices a byte array.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>The slices byte array.</returns>
        private static byte[] Slice(byte[] source, int start, int end)
        {
            if (end < 0) end = source.Length + end;

            var len = end - start;
            var res = new byte[len];
            for (var i = 0; i < len; i++) res[i] = source[i + start];

            return res;
        }

        #endregion
    }
}