using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace Shaman.Runtime
{
    public static class Charts
    {
        public static void ShowChart<T>(this IEnumerable<T> items)
        {
            var chart = new Chart();

            var area = new ChartArea();

            var fields = GetFields(typeof(T));

            var axisXfield = fields[0];

            area.AxisX.Title = axisXfield.Name;


            



            foreach (var field in fields)
            {
                if (field == axisXfield) continue;
                var series = new Series();
                series.Name = field != null ? field.Name : "Count";
                series.BorderWidth = 3;
                series.IsVisibleInLegend = fields.Count > 2;
                series.IsValueShownAsLabel = false;
                if (axisXfield.Type == typeof(DateTime))
                {
                    series.ChartType = fields.Count <= 2 ? SeriesChartType.Area : SeriesChartType.Line;
                    series.XValueType = ChartValueType.DateTime;
                }
                else
                {
                    series.ChartType = SeriesChartType.Column;
                }
   
                chart.Series.Add(series);
            }

            
            
            chart.ChartAreas.Add(area);



            foreach (var item in items)
            {
                var seriesIndex = 0;
                var x = axisXfield.Get(item);
                if (x == null) continue;

                

                foreach (var field in fields)
                {
                    if (field == axisXfield) continue;
                    var value = field != null ? field.Get(item) : ((float)((IEnumerable<object>)item).Count());
                    //var a = value as ILogarithmicRounding;
                    //if (a != null) value = a.Value;
                    if (value is TimeSpan) value = ((TimeSpan)value).TotalSeconds;
                    var series = chart.Series[seriesIndex];
                    var dp = new DataPoint();
                    dp.SetValueXY(x, value);
                    dp.ToolTip = x + ": " + value;
                    series.Points.Add(dp);
                    seriesIndex++;
                }
            }



            area.AxisX.LabelAutoFitStyle = LabelAutoFitStyles.LabelsAngleStep45 | LabelAutoFitStyles.DecreaseFont | LabelAutoFitStyles.IncreaseFont;
            //area.AxisX.IntervalType = DateTimeIntervalType.Weeks;
            area.AxisX.Interval = 6;
            area.AxisX.IntervalType = DateTimeIntervalType.Months;
            //area.AxisX.IntervalAutoMode = IntervalAutoMode.FixedCount;
            //area.AxisX.LabelStyle = new LabelStyle() { Angle = 45, IntervalType = DateTimeIntervalType.Weeks };
            //area.AxisX.Interval = 50;




            ShowChart(chart);
        }

        public static void ShowChart(this Chart chart)
        {
            using (var form = new Form())
            {
                form.Width = 1000;
                form.Height = 600;
                form.StartPosition = FormStartPosition.CenterScreen;
                chart.Dock = DockStyle.Fill;
                form.Controls.Add(chart);

                form.ShowDialog();
            }
        }

        internal class Field
        {
            public string Name;
            public Func<object, object> Get;
            public Type Type;
            public Action<object, object> Set;
            public int ColumnWidth;
            public bool IsNumeric;
            public MemberInfo Member;
        }

        internal static List<Field> GetFields(Type type)
        {

#if !STANDALONE
            if (type.Is<Entity>())
            {
                return EntityType.FromNativeType(type).Fields.Select(x => new Field()
                {
                    Name = x.Name,
                    Get = new Func<object, object>(y =>
                    {
                        var ent = (Entity)y;
                        return x.IsStoredIn(ent) ? x.GetStoredValueDirect(ent) : null;
                    }),
                    Type = x.FieldType.NativeType
                }).ToList();
            };
#endif

            var emptyArray = new object[] { };
            var fields =
            type.GetTypeInfo().IsPrimitive || type.GetTypeInfo().IsEnum || type == typeof(string) ? new[] { new Field { Name = "Value", Get = new Func<object, object>(y => y), Type = type } }.ToList() :
            type.GetFields(BindingFlags.Instance | BindingFlags.Public)
#if !STANDALONE
            .Where(x => x.GetCustomAttribute<RestrictedAccessAttribute>() == null)
#endif
            .Select(x => new Field { Name = x.Name, Member = x, Get = ReflectionHelper.GetGetter<object, object>(x), Type = x.FieldType })
            .Union(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                 x.GetIndexParameters().Length == 0
#if !STANDALONE
                 && x.DeclaringType != typeof(Entity)
                 && x.GetMethod.GetCustomAttribute<RestrictedAccessAttribute>() == null
#endif
)
             .Select(x => new Field { Name = x.Name, Member = x, Get = ReflectionHelper.GetGetter<object, object>(x), Type = x.PropertyType }))
            .ToList();
            foreach (var field in fields)
            {
                var t = field.Type;
                if (t.GetTypeInfo().IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) t = Nullable.GetUnderlyingType(t);
                field.IsNumeric = NumericTypes.Contains(t);
            }
            return fields;

        }


        internal static readonly Type[] NumericTypes = new[]{
           typeof(Byte),typeof(SByte),
           typeof(Int16),typeof(Int32),typeof(Int64),
           typeof(UInt16),typeof(UInt32),typeof(UInt64),
           typeof(Single),typeof(Double),typeof(Decimal)
        };


    }


}
