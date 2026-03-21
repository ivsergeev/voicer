using System;

namespace Voicer.Core.Utilities
{
    /// <summary>
    /// Provides common sorting algorithms.
    /// </summary>
    public static class Sorting
    {
        /// <summary>
        /// Sorts an array using the bubble sort algorithm.
        /// </summary>
        /// <typeparam name="T">Type of array elements that implement IComparable&lt;T&gt;.</typeparam>
        /// <param name="array">The array to sort.</param>
        public static void BubbleSort<T>(T[] array) where T : IComparable<T>
        {
            if (array == null || array.Length <= 1)
                return;

            bool swapped;
            int n = array.Length;

            do
            {
                swapped = false;
                for (int i = 1; i < n; i++)
                {
                    if (array[i - 1].CompareTo(array[i]) > 0)
                    {
                        T temp = array[i - 1];
                        array[i - 1] = array[i];
                        array[i] = temp;
                        swapped = true;
                    }
                }
                // After each pass, the largest element is at the end; reduce range
                n--;
            } while (swapped);
        }
    }
}
