using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;

namespace Shaman.Runtime
{
    [ProtoContract]
    public class TimeArray<T>
    {
        [ProtoMember(1)]
        private DateTime startDate;

        [ProtoMember(2)]
        private TimeSpan granularity;

        [ProtoMember(3)]
        private T[] data;

        public T[] BackingArray => data;

        public DateTime StartDate => startDate;

        public IEnumerable<KeyValuePair<DateTime, ArraySegment<T>>> GetWithGranularityMultiple(TimeSpan largerGranularity)
        {
            var s = (int)(largerGranularity.Ticks / granularity.Ticks);
            if (s == 0) throw new ArgumentException();
            return GetWithGranularityMultiple(s);
        }

        public IEnumerable<KeyValuePair<DateTime, ArraySegment<T>>> GetWithGranularityMultiple(int multiple)
        {
            var now = GetIndex(DateTime.UtcNow);
            for (int i = 0; i < data.Length; i += multiple)
            {
                var end = i + multiple;
                if (end >= data.Length) break;
                if (i > now) break;
                yield return new KeyValuePair<DateTime, ArraySegment<T>>(GetDate(i), new ArraySegment<T>(data, i, multiple));
            }
        }



        public TimeSpan Granularity => granularity;

        // For serialization
        public TimeArray()
        {
        }
        public TimeArray(DateTime startDate, TimeSpan granularity)
        {
            this.startDate = startDate;
            this.granularity = granularity;
        }

        private DateTime GetDate(int index)
        {
            return new DateTime(startDate.Ticks + index * granularity.Ticks, DateTimeKind.Utc);
        }

        private int GetIndex(DateTime date)
        {
            var idx = checked((int)((date - startDate).Ticks / granularity.Ticks));
            if (idx < 0) return -1;
            if (data == null)
            {
                var duration = (DateTime.UtcNow - startDate).Ticks * 1.2;
                var maxidx = checked((int)(duration / granularity.Ticks));
                data = new T[maxidx];
            }
            if (idx >= data.Length)
            {
                if (date > DateTime.UtcNow) return -1;
                Array.Resize(ref data, (int)(idx * 1.2));
            }
            return idx;
        }



        public IEnumerable<KeyValuePair<DateTime, T>> Data
        {
            get
            {
                var maxidx = GetIndex(DateTime.UtcNow);
                for (int i = 0; i < maxidx; i++)
                {
                    yield return new KeyValuePair<DateTime, T>(GetDate(i), data[i]);
                }
            }
        }

        public T this[DateTime date]
        {
            get
            {
                var idx = GetIndex(date);
                if (idx == -1) return default(T);
                return data[idx];
            }
            set
            {
                var idx = GetIndex(date);
                if (idx != -1) data[idx] = value;
            }
        }



    }
}
