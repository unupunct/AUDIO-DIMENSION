namespace AudioDimension.Audio.Analysis;

/// <summary>Radix-2 FFT with a Hann window. Magnitudes are normalised so a full-scale sine reads ~1.0.</summary>
public sealed class FftProcessor
{
    public int Size { get; }
    private readonly float[] _window;
    private readonly float[] _cos, _sin;
    private readonly int[] _rev;
    private readonly float[] _re, _im;
    private readonly float _norm;

    public FftProcessor(int size)
    {
        if (size < 8 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two >= 8", nameof(size));
        Size = size;
        _window = new float[size];
        double sum = 0;
        for (int i = 0; i < size; i++)
        {
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size));
            sum += _window[i];
        }
        _norm = (float)(2.0 / sum);
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            _cos[i] = (float)Math.Cos(2 * Math.PI * i / size);
            _sin[i] = (float)-Math.Sin(2 * Math.PI * i / size);
        }
        _rev = new int[size];
        int bits = (int)Math.Log2(size);
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            _rev[i] = r;
        }
        _re = new float[size];
        _im = new float[size];
    }

    /// <summary>Computes Size/2 amplitude magnitudes from the last Size samples of <paramref name="input"/>.</summary>
    public void ComputeMagnitudes(ReadOnlySpan<float> input, Span<float> magnitudes)
    {
        if (input.Length < Size) throw new ArgumentException("Input shorter than FFT size");
        if (magnitudes.Length < Size / 2) throw new ArgumentException("Magnitude buffer too small");
        var src = input.Slice(input.Length - Size, Size);
        for (int i = 0; i < Size; i++)
        {
            _re[_rev[i]] = src[i] * _window[i];
            _im[_rev[i]] = 0;
        }
        for (int len = 2; len <= Size; len <<= 1)
        {
            int half = len >> 1, step = Size / len;
            for (int i = 0; i < Size; i += len)
            {
                for (int j = 0, k = 0; j < half; j++, k += step)
                {
                    float wr = _cos[k], wi = _sin[k];
                    int a = i + j, b = a + half;
                    float tr = _re[b] * wr - _im[b] * wi;
                    float ti = _re[b] * wi + _im[b] * wr;
                    _re[b] = _re[a] - tr; _im[b] = _im[a] - ti;
                    _re[a] += tr; _im[a] += ti;
                }
            }
        }
        for (int i = 0; i < Size / 2; i++)
            magnitudes[i] = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * _norm;
    }
}
