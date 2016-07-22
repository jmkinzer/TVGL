﻿/*******************************************************************************
*                                                                              *
* Author    :  Angus Johnson                                                   *
* Version   :  6.2.1                                                           *
* Date      :  31 October 2014                                                 *
* Website   :  http://www.angusj.com                                           *
* Copyright :  Angus Johnson 2010-2014                                         *
*                                                                              *
* License:                                                                     *
* Use, modification & distribution is subject to Boost Software License Ver 1. *
* http://www.boost.org/LICENSE_1_0.txt                                         *
*                                                                              *
* Attributions:                                                                *
* The code in this library is an extension of Bala Vatti's clipping algorithm: *
* "A generic solution to polygon clipping"                                     *
* Communications of the ACM, Vol 35, Issue 7 (July 1992) pp 56-63.             *
* http://portal.acm.org/citation.cfm?id=129906                                 *
*                                                                              *
* Computer graphics and geometric modeling: implementation and algorithms      *
* By Max K. Agoston                                                            *
* Springer; 1 edition (January 4, 2005)                                        *
* http://books.google.com/books?q=vatti+clipping+agoston                       *
*                                                                              *
* See also:                                                                    *
* "Polygon Offsetting by Computing Winding Numbers"                            *
* Paper no. DETC2005-85513 pp. 565-575                                         *
* ASME 2005 International Design Engineering Technical Conferences             *
* and Computers and Information in Engineering Conference (IDETC/CIE2005)      *
* September 24-28, 2005 , Long Beach, California, USA                          *
* http://www.me.berkeley.edu/~mcmains/pubs/DAC05OffsetPolygon.pdf              *
*                                                                              *
*******************************************************************************/
//Note that a newer paper "An Improved Polygon Clipping Algorithm Based on Affine Transformation" 
//Claims to beat the Vatti Altgorithms

/*******************************************************************************
*                                                                              *
* This is a translation of the Delphi Clipper library and the naming style     *
* used has retained a Delphi flavour.                                          *
*                                                                              *
*******************************************************************************/

//use_int32: When enabled 32bit ints are used instead of 64bit ints. This
//improve performance but coordinate values are limited to the range +/- 46340
//#define use_int32

//use_xyz: adds a Z member to IntPoint. Adds a minor cost to performance.
//#define use_xyz

//use_deprecated: Enables temporary support for the obsolete functions
//#define use_deprecated


using System;
using System.Collections.Generic;
using System.Linq;
using StarMathLib;
using TVGL.Boolean_Operations.Clipper;

//using System.Text;          //for Int128.AsString() & StringBuilder
//using System.IO;            //debugging with streamReader & StreamWriter
//using System.Windows.Forms; //debugging to clipboard

namespace TVGL.Boolean_Operations
{
    #region internal Interface with Clipper

    /// <summary>
    /// Interface to the 2D offset/clipping library: Clipper http://www.angusj.com/delphi/clipper.php
    /// </summary>
    public static class PolygonOffset
    {
        /// <summary>
        /// Offets the given loop by the given offset, rounding corners.
        /// </summary>
        /// <param name="loop"></param>
        /// <param name="offset"></param>
        /// <param name="scale"></param>
        /// <returns></returns>
        public static List<Point> Round(IList<Point> loop, double offset, double scale = 100000)
        {
            var loops = new List<List<Point>> { new List<Point>(loop) };
            var offsetLoops = Round(loops, offset, scale);
            return offsetLoops.First();
        }


        /// <summary>
        /// Offsets all loops by the given offset value. Rounds the corners.
        /// Offest value may be positive or negative.
        /// Loops must be ordered CCW positive.
        /// </summary>
        /// <param name="loops"></param>
        /// <param name="offset"></param>
        /// <param name="scale"></param>
        /// <returns></returns>
        public static List<List<Point>> Round(List<List<Point>> loops, double offset, double scale = 100000)
        {
            //Convert Points (TVGL) to IntPoints (Clipper)
            var polygons =
                loops.Select(loop => loop.Select(point => new IntPoint(point.X * scale, point.Y * scale)).ToList()).ToList();

            //Begin an evaluation
            var solution = new List<List<IntPoint>>();
            var clip = new ClipperOffset();
            clip.AddPaths(polygons, JoinType.Round, EndType.ClosedPolygon);
            clip.Execute(ref solution, offset * scale);

            var offsetLoops = new List<List<Point>>();
            foreach (var loop in solution)
            {
                var offsetLoop = new List<Point>();
                for (var i = 0; i < loop.Count; i++)
                {
                    var intPoint = loop[i];
                    var x = Convert.ToDouble(intPoint.X) / scale;
                    var y = Convert.ToDouble(intPoint.Y) / scale;
                    offsetLoop.Add(new Point(new List<double> { x, y, 0.0 }));
                }
                offsetLoops.Add(offsetLoop);
            }
            return offsetLoops;
        }
    }

    #endregion
}

namespace TVGL.Boolean_Operations.Clipper
{
    using Path = List<IntPoint>;
    using Paths = List<List<IntPoint>>;

    #region DoublePoint Class
    internal struct DoublePoint
    {
        internal double X;
        internal double Y;

        internal DoublePoint(double x = 0, double y = 0)
        {
            X = x; Y = y;
        }
        internal DoublePoint(DoublePoint dp)
        {
            X = dp.X; Y = dp.Y;
        }
        internal DoublePoint(IntPoint ip)
        {
            X = ip.X; Y = ip.Y;
        }
    }
    #endregion

    #region PolyTree & PolyNode classes
    internal class PolyTree : PolyNode
    {
        internal List<PolyNode> MAllPolys = new List<PolyNode>();

        ~PolyTree()
        {
            Clear();
        }

        internal void Clear()
        {
            for (var i = 0; i < MAllPolys.Count; i++)
                MAllPolys[i] = null;
            MAllPolys.Clear();
            MChilds.Clear();
        }

        internal PolyNode GetFirst()
        {
            if (MChilds.Count > 0)
                return MChilds[0];
            return null;
        }

        internal int Total
        {
            get
            {
                var result = MAllPolys.Count;
                //with negative offsets, ignore the hidden outer polygon ...
                if (result > 0 && MChilds[0] != MAllPolys[0]) result--;
                return result;
            }
        }
    }

    internal class PolyNode
    {
        internal PolyNode MParent;
        internal Path MPolygon = new Path();
        internal int MIndex;
        internal JoinType MJointype;
        internal EndType MEndtype;
        internal List<PolyNode> MChilds = new List<PolyNode>();

        internal bool IsHoleNode()
        {
            bool result = true;
            PolyNode node = MParent;
            while (node != null)
            {
                result = !result;
                node = node.MParent;
            }
            return result;
        }

        internal int ChildCount
        {
            get { return MChilds.Count; }
        }

        internal Path Contour
        {
            get { return MPolygon; }
        }

        internal void AddChild(PolyNode child)
        {
            int cnt = MChilds.Count;
            MChilds.Add(child);
            child.MParent = this;
            child.MIndex = cnt;
        }

        internal PolyNode GetNext()
        {
            if (MChilds.Count > 0)
                return MChilds[0];
            else
                return GetNextSiblingUp();
        }

        internal PolyNode GetNextSiblingUp()
        {
            if (MParent == null)
                return null;
            if (MIndex == MParent.MChilds.Count - 1)
                return MParent.GetNextSiblingUp();
            return MParent.MChilds[MIndex + 1];
        }

        internal List<PolyNode> Childs
        {
            get { return MChilds; }
        }

        internal PolyNode Parent
        {
            get { return MParent; }
        }

        internal bool IsHole
        {
            get { return IsHoleNode(); }
        }

        internal bool IsOpen { get; set; }
    }
    #endregion

    #region Int128 class Structure
    //------------------------------------------------------------------------------
    // Int128 struct (enables safe math on signed 64bit integers)
    // eg Int128 val1((Int64)9223372036854775807); //ie 2^63 -1
    //    Int128 val2((Int64)9223372036854775807);
    //    Int128 val3 = val1 * val2;
    //    val3.ToString => "85070591730234615847396907784232501249" (8.5e+37)
    //------------------------------------------------------------------------------

    internal struct Int128
    {
        private long hi;
        private ulong lo;

        internal Int128(long _lo)
        {
            lo = (ulong)_lo;
            if (_lo < 0) hi = -1;
            else hi = 0;
        }

        internal Int128(long _hi, ulong _lo)
        {
            lo = _lo;
            hi = _hi;
        }

        internal Int128(Int128 val)
        {
            hi = val.hi;
            lo = val.lo;
        }

        internal bool IsNegative()
        {
            return hi < 0;
        }

        public static bool operator ==(Int128 val1, Int128 val2)
        {
            if ((object)val1 == (object)val2) return true;
            return (val1.hi == val2.hi && val1.lo == val2.lo);
        }

        public static bool operator !=(Int128 val1, Int128 val2)
        {
            return !(val1 == val2);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Int128))
                return false;
            var i128 = (Int128)obj;
            return (i128.hi == hi && i128.lo == lo);
        }

        public override int GetHashCode()
        {
            return hi.GetHashCode() ^ lo.GetHashCode();
        }

        public static bool operator >(Int128 val1, Int128 val2)
        {
            if (val1.hi != val2.hi)
                return val1.hi > val2.hi;
            return val1.lo > val2.lo;
        }

        public static bool operator <(Int128 val1, Int128 val2)
        {
            if (val1.hi != val2.hi)
                return val1.hi < val2.hi;
            return val1.lo < val2.lo;
        }

        public static Int128 operator +(Int128 lhs, Int128 rhs)
        {
            lhs.hi += rhs.hi;
            lhs.lo += rhs.lo;
            if (lhs.lo < rhs.lo) lhs.hi++;
            return lhs;
        }

        public static Int128 operator -(Int128 lhs, Int128 rhs)
        {
            return lhs + -rhs;
        }

        public static Int128 operator -(Int128 val)
        {
            return val.lo == 0 ? new Int128(-val.hi, 0) : new Int128(~val.hi, ~val.lo + 1);
        }

        public static explicit operator double(Int128 val)
        {
            const double shift64 = 18446744073709551616.0; //2^64
            if (val.hi >= 0) return val.lo + val.hi * shift64;
            if (val.lo == 0)
                return val.hi * shift64;
            return -(~val.lo + ~val.hi * shift64);
        }

        //nb: Constructing two new Int128 objects every time we want to multiply longs  
        //is slow. So, although calling the Int128Mul method doesn't look as clean, the 
        //code runs significantly faster than if we'd used the * operator.

        internal static Int128 Int128Mul(long lhs, long rhs)
        {
            bool negate = (lhs < 0) != (rhs < 0);
            if (lhs < 0) lhs = -lhs;
            if (rhs < 0) rhs = -rhs;
            ulong int1Hi = (ulong)lhs >> 32;
            ulong int1Lo = (ulong)lhs & 0xFFFFFFFF;
            ulong int2Hi = (ulong)rhs >> 32;
            ulong int2Lo = (ulong)rhs & 0xFFFFFFFF;

            //nb: see comments in clipper.pas
            ulong a = int1Hi * int2Hi;
            ulong b = int1Lo * int2Lo;
            ulong c = int1Hi * int2Lo + int1Lo * int2Hi;

            ulong lo;
            long hi;
            hi = (long)(a + (c >> 32));

            unchecked { lo = (c << 32) + b; }
            if (lo < b) hi++;
            Int128 result = new Int128(hi, lo);
            return negate ? -result : result;
        }
    }
    #endregion

    #region Integer Point Class
    /// <summary>
    /// Integer Point with X and Y coordinates
    /// </summary>
    public struct IntPoint
    {
        /// <summary>
        /// X value for integer point
        /// </summary>
        public long X;
        /// <summary>
        /// Y value for integer point
        /// </summary>
        public long Y;
        internal IntPoint(long X, long Y)
        {
            this.X = X; this.Y = Y;
        }
        internal IntPoint(double x, double y)
        {
            X = (long)x; Y = (long)y;
        }

        internal IntPoint(IntPoint pt)
        {
            X = pt.X; Y = pt.Y;
        }

        /// <summary>
        /// Gets whether int points are equal
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator ==(IntPoint a, IntPoint b)
        {
            return ((double)a.X).IsPracticallySame(b.X) && ((double)a.Y).IsPracticallySame(b.Y);
        }

        /// <summary>
        /// Gets whether int point are not equal
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator !=(IntPoint a, IntPoint b)
        {
            return !((double)a.X).IsPracticallySame(b.X) || !((double)a.Y).IsPracticallySame(b.Y);
        }

        /// <summary>
        /// Checks if this intPoint is equalt to the given object.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            if (obj is IntPoint)
            {
                IntPoint a = (IntPoint)obj;
                return (X == a.X) && (Y == a.Y);
            }
            else return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            //simply prevents a compiler warning
            return base.GetHashCode();
        }

    }// end struct IntPoint
    #endregion

    #region Integer Rectangle Class
    internal struct IntRect
    {
        internal long left;
        internal long top;
        internal long right;
        internal long bottom;

        internal IntRect(long l, long t, long r, long b)
        {
            left = l; top = t;
            right = r; bottom = b;
        }
        internal IntRect(IntRect ir)
        {
            left = ir.left; top = ir.top;
            right = ir.right; bottom = ir.bottom;
        }
    }
    #endregion

    #region Internal Enum Values
    internal enum ClipType { Intersection, Union, Difference, Xor };
    internal enum PolyType { Subject, Clip };

    //By far the most widely used winding rules for polygon filling are
    //EvenOdd & NonZero (GDI, GDI+, XLib, OpenGL, Cairo, AGG, Quartz, SVG, Gr32)
    //Others rules include Positive, Negative and ABS_GTR_EQ_TWO (only in OpenGL)
    //see http://glprogramming.com/red/chapter11.html
    internal enum PolyFillType { EvenOdd, NonZero, Positive, Negative };

    internal enum JoinType { Square, Round, Miter };
    internal enum EndType { ClosedPolygon, ClosedLine, OpenButt, OpenSquare, OpenRound };

    internal enum EdgeSide { Left, Right };
    internal enum Direction { RightToLeft, LeftToRight };
    #endregion

    #region T Edge Class
    internal class TEdge
    {
        internal IntPoint Bot;
        internal IntPoint Curr;
        internal IntPoint Top;
        internal IntPoint Delta;
        internal double Dx;
        internal PolyType PolyTyp;
        internal EdgeSide Side;
        internal int WindDelta; //1 or -1 depending on winding direction
        internal int WindCnt;
        internal int WindCnt2; //winding count of the opposite polytype
        internal int OutIdx;
        internal TEdge Next;
        internal TEdge Prev;
        internal TEdge NextInLML;
        internal TEdge NextInAEL;
        internal TEdge PrevInAEL;
        internal TEdge NextInSEL;
        internal TEdge PrevInSEL;
    }
    #endregion

    #region IntersectZ Node Class
    internal class IntersectNode
    {
        internal TEdge Edge1;
        internal TEdge Edge2;
        internal IntPoint Pt;
    }
    #endregion

    #region Other Internal Classes
    internal class MyIntersectNodeSort : IComparer<IntersectNode>
    {
        public int Compare(IntersectNode node1, IntersectNode node2)
        {
            var i = node2.Pt.Y - node1.Pt.Y;
            if (i > 0) return 1;
            if (i < 0) return -1;
            return 0;
        }
    }

    internal class LocalMinima
    {
        internal long Y;
        internal TEdge LeftBound;
        internal TEdge RightBound;
        internal LocalMinima Next;
    }

    internal class Scanbeam
    {
        internal long Y;
        internal Scanbeam Next;
    }

    internal class OutRec
    {
        internal int Idx;
        internal bool IsHole;
        internal bool IsOpen;
        internal OutRec FirstLeft; //see comments in clipper.pas
        internal OutPt Pts;
        internal OutPt BottomPt;
        internal PolyNode PolyNode;
    }

    internal class OutPt
    {
        internal int Idx;
        internal IntPoint Pt;
        internal OutPt Next;
        internal OutPt Prev;
    }

    internal class Join
    {
        internal OutPt OutPt1;
        internal OutPt OutPt2;
        internal IntPoint OffPt;
    }
    #endregion

    #region ClipperBase Class
    internal class ClipperBase
    {
        protected const double Horizontal = -3.4E+38;
        protected const int Skip = -2;
        protected const int Unassigned = -1;
        protected const double Tolerance = 1.0E-20;
        internal static bool near_zero(double val) { return (val > -Tolerance) && (val < Tolerance); }
        internal const long LoRange = 0x3FFFFFFF;
        internal const long HiRange = 0x3FFFFFFFFFFFFFFFL;
        internal LocalMinima MMinimaList;
        internal LocalMinima MCurrentLm;
        internal List<List<TEdge>> MEdges = new List<List<TEdge>>();
        internal bool MUseFullRange;
        internal bool MHasOpenPaths;

        internal bool PreserveCollinear
        {
            get;
            set;
        }

        internal void Swap(ref long val1, ref long val2)
        {
            var tmp = val1;
            val1 = val2;
            val2 = tmp;
        }

        internal static bool IsHorizontal(TEdge e)
        {
            return e.Delta.Y == 0;
        }

        internal bool PointIsVertex(IntPoint pt, OutPt pp)
        {
            var pp2 = pp;
            do
            {
                if (pp2.Pt == pt) return true;
                pp2 = pp2.Next;
            }
            while (pp2 != pp);
            return false;
        }

        internal bool PointOnLineSegment(IntPoint pt,
            IntPoint linePt1, IntPoint linePt2, bool useFullRange)
        {
            if (useFullRange)
                return ((pt.X == linePt1.X) && (pt.Y == linePt1.Y)) ||
                    ((pt.X == linePt2.X) && (pt.Y == linePt2.Y)) ||
                    (((pt.X > linePt1.X) == (pt.X < linePt2.X)) &&
                    ((pt.Y > linePt1.Y) == (pt.Y < linePt2.Y)) &&
                    ((Int128.Int128Mul((pt.X - linePt1.X), (linePt2.Y - linePt1.Y)) ==
                    Int128.Int128Mul((linePt2.X - linePt1.X), (pt.Y - linePt1.Y)))));
            return ((pt.X == linePt1.X) && (pt.Y == linePt1.Y)) ||
                   ((pt.X == linePt2.X) && (pt.Y == linePt2.Y)) ||
                   (((pt.X > linePt1.X) == (pt.X < linePt2.X)) &&
                    ((pt.Y > linePt1.Y) == (pt.Y < linePt2.Y)) &&
                    ((pt.X - linePt1.X) * (linePt2.Y - linePt1.Y) ==
                     (linePt2.X - linePt1.X) * (pt.Y - linePt1.Y)));
        }

        //------------------------------------------------------------------------------

        internal bool PointOnPolygon(IntPoint pt, OutPt pp, bool UseFullRange)
        {
            OutPt pp2 = pp;
            while (true)
            {
                if (PointOnLineSegment(pt, pp2.Pt, pp2.Next.Pt, UseFullRange))
                    return true;
                pp2 = pp2.Next;
                if (pp2 == pp) break;
            }
            return false;
        }
        //------------------------------------------------------------------------------

        internal static bool SlopesEqual(TEdge e1, TEdge e2, bool UseFullRange)
        {
            if (UseFullRange)
                return Int128.Int128Mul(e1.Delta.Y, e2.Delta.X) ==
                    Int128.Int128Mul(e1.Delta.X, e2.Delta.Y);
            return ((double)e1.Delta.Y * e2.Delta.X).IsPracticallySame(e1.Delta.X * e2.Delta.Y);
        }
        //------------------------------------------------------------------------------

        protected static bool SlopesEqual(IntPoint pt1, IntPoint pt2,
            IntPoint pt3, bool UseFullRange)
        {
            if (UseFullRange)
                return Int128.Int128Mul(pt1.Y - pt2.Y, pt2.X - pt3.X) ==
                       Int128.Int128Mul(pt1.X - pt2.X, pt2.Y - pt3.Y);
                return ((double) (pt1.Y - pt2.Y)*(pt2.X - pt3.X) - (pt1.X - pt2.X)*(pt2.Y - pt3.Y)).IsNegligible();
        }
        //------------------------------------------------------------------------------

        protected static bool SlopesEqual(IntPoint pt1, IntPoint pt2,
            IntPoint pt3, IntPoint pt4, bool UseFullRange)
        {
            if (UseFullRange)
                return Int128.Int128Mul(pt1.Y - pt2.Y, pt3.X - pt4.X) ==
                  Int128.Int128Mul(pt1.X - pt2.X, pt3.Y - pt4.Y);
            return ((double)(pt1.Y - pt2.Y) * (pt3.X - pt4.X) - (pt1.X - pt2.X) * (pt3.Y - pt4.Y)).IsNegligible();
        }

        internal ClipperBase() //constructor (nb: no external instantiation)
        {
            MMinimaList = null;
            MCurrentLm = null;
            MUseFullRange = false;
            MHasOpenPaths = false;
        }

        internal virtual void Clear()
        {
            DisposeLocalMinimaList();
            foreach (var t in MEdges)
            {
                for (var j = 0; j < t.Count; ++j) t[j] = null;
                t.Clear();
            }
            MEdges.Clear();
            MUseFullRange = false;
            MHasOpenPaths = false;
        }

        private void DisposeLocalMinimaList()
        {
            while (MMinimaList != null)
            {
                LocalMinima tmpLm = MMinimaList.Next;
                MMinimaList = null;
                MMinimaList = tmpLm;
            }
            MCurrentLm = null;
        }

        private static void RangeTest(IntPoint pt, ref bool useFullRange)
        {
            while (true)
            {
                if (useFullRange)
                {
                    if (pt.X > HiRange || pt.Y > HiRange || -pt.X > HiRange || -pt.Y > HiRange)
                        throw new ClipperException("Coordinate outside allowed range");
                }
                else if (pt.X > LoRange || pt.Y > LoRange || -pt.X > LoRange || -pt.Y > LoRange)
                {
                    useFullRange = true;
                    continue;
                }
                break;
            }
        }

        private static void InitEdge(TEdge e, TEdge eNext,
        TEdge ePrev, IntPoint pt)
        {
            e.Next = eNext;
            e.Prev = ePrev;
            e.Curr = pt;
            e.OutIdx = Unassigned;
        }

        private void InitEdge2(TEdge e, PolyType polyType)
        {
            if (e.Curr.Y >= e.Next.Curr.Y)
            {
                e.Bot = e.Curr;
                e.Top = e.Next.Curr;
            }
            else
            {
                e.Top = e.Curr;
                e.Bot = e.Next.Curr;
            }
            SetDx(e);
            e.PolyTyp = polyType;
        }

        private static TEdge FindNextLocMin(TEdge E)
        {
            for (;;)
            {
                while (E.Bot != E.Prev.Bot || E.Curr == E.Top) E = E.Next;
                if (!E.Dx.IsPracticallySame(Horizontal) && !E.Prev.Dx.IsPracticallySame(Horizontal)) break;
                while (E.Prev.Dx.IsPracticallySame(Horizontal)) E = E.Prev;
                var e2 = E;
                while (E.Dx.IsPracticallySame(Horizontal)) E = E.Next;
                if (E.Top.Y == E.Prev.Bot.Y) continue; //ie just an intermediate horz.
                if (e2.Prev.Bot.X < E.Bot.X) E = e2;
                break;
            }
            return E;
        }

        private TEdge ProcessBound(TEdge E, bool leftBoundIsForward)
        {
            TEdge eStart, result = E;
            TEdge horz;

            if (result.OutIdx == Skip)
            {
                //check if there are edges beyond the skip edge in the bound and if so
                //create another LocMin and calling ProcessBound once more ...
                E = result;
                if (leftBoundIsForward)
                {
                    while (E.Top.Y == E.Next.Bot.Y) E = E.Next;
                    while (E != result && E.Dx.IsPracticallySame(Horizontal)) E = E.Prev;
                }
                else
                {
                    while (E.Top.Y == E.Prev.Bot.Y) E = E.Prev;
                    while (E != result && E.Dx.IsPracticallySame(Horizontal)) E = E.Next;
                }
                if (E == result)
                {
                    result = leftBoundIsForward ? E.Next : E.Prev;
                }
                else
                {
                    //there are more edges in the bound beyond result starting with E
                    E = leftBoundIsForward ? result.Next : result.Prev;
                    var locMin = new LocalMinima
                    {
                        Next = null,
                        Y = E.Bot.Y,
                        LeftBound = null,
                        RightBound = E
                    };
                    E.WindDelta = 0;
                    result = ProcessBound(E, leftBoundIsForward);
                    InsertLocalMinima(locMin);
                }
                return result;
            }

            if (E.Dx.IsPracticallySame(Horizontal))
            {
                //We need to be careful with open paths because this may not be a
                //true local minima (ie E may be following a skip edge).
                //Also, consecutive horz. edges may start heading left before going right.
                eStart = leftBoundIsForward ? E.Prev : E.Next;
                if (eStart.OutIdx != Skip)
                {
                    if (eStart.Dx.IsPracticallySame(Horizontal)) //ie an adjoining horizontal skip edge
                    {
                        if (eStart.Bot.X != E.Bot.X && eStart.Top.X != E.Bot.X)
                            ReverseHorizontal(E);
                    }
                    else if (eStart.Bot.X != E.Bot.X)
                        ReverseHorizontal(E);
                }
            }

            eStart = E;
            if (leftBoundIsForward)
            {
                while (result.Top.Y == result.Next.Bot.Y && result.Next.OutIdx != Skip)
                    result = result.Next;
                if (result.Dx.IsPracticallySame(Horizontal) && result.Next.OutIdx != Skip)
                {
                    //nb: at the top of a bound, horizontals are added to the bound
                    //only when the preceding edge attaches to the horizontal's left vertex
                    //unless a Skip edge is encountered when that becomes the top divide
                    horz = result;
                    while (horz.Prev.Dx.IsPracticallySame(Horizontal)) horz = horz.Prev;
                    if (horz.Prev.Top.X == result.Next.Top.X)
                    {
                    }
                    else if (horz.Prev.Top.X > result.Next.Top.X) result = horz.Prev;
                }
                while (E != result)
                {
                    E.NextInLML = E.Next;
                    if (E.Dx.IsPracticallySame(Horizontal) && E != eStart && E.Bot.X != E.Prev.Top.X)
                        ReverseHorizontal(E);
                    E = E.Next;
                }
                if (E.Dx.IsPracticallySame(Horizontal) && E != eStart && E.Bot.X != E.Prev.Top.X)
                    ReverseHorizontal(E);
                result = result.Next; //move to the edge just beyond current bound
            }
            else
            {
                while (result.Top.Y == result.Prev.Bot.Y && result.Prev.OutIdx != Skip)
                    result = result.Prev;
                if (result.Dx.IsPracticallySame(Horizontal) && result.Prev.OutIdx != Skip)
                {
                    horz = result;
                    while (horz.Next.Dx.IsPracticallySame(Horizontal)) horz = horz.Next;
                    if (horz.Next.Top.X == result.Prev.Top.X)
                    {
                        result = horz.Next;
                    }
                    else if (horz.Next.Top.X > result.Prev.Top.X) result = horz.Next;
                }

                while (E != result)
                {
                    E.NextInLML = E.Prev;
                    if (E.Dx.IsPracticallySame(Horizontal) && E != eStart && E.Bot.X != E.Next.Top.X)
                        ReverseHorizontal(E);
                    E = E.Prev;
                }
                if (E.Dx.IsPracticallySame(Horizontal) && E != eStart && E.Bot.X != E.Next.Top.X)
                    ReverseHorizontal(E);
                result = result.Prev; //move to the edge just beyond current bound
            }
            return result;
        }
        //------------------------------------------------------------------------------


        internal bool AddPath(Path pg, PolyType polyType, bool Closed)
        {
            if (!Closed && polyType == PolyType.Clip)
                throw new ClipperException("AddPath: Open paths must be subject.");

            var highI = pg.Count - 1;
            if (Closed) while (highI > 0 && (pg[highI] == pg[0])) --highI;
            while (highI > 0 && (pg[highI] == pg[highI - 1])) --highI;
            if ((Closed && highI < 2) || (!Closed && highI < 1)) return false;

            //create a new edge array ...
            var edges = new List<TEdge>(highI + 1);
            for (var i = 0; i <= highI; i++) edges.Add(new TEdge());

            var isFlat = true;

            //1. Basic (first) edge initialization ...
            edges[1].Curr = pg[1];
            RangeTest(pg[0], ref MUseFullRange);
            RangeTest(pg[highI], ref MUseFullRange);
            InitEdge(edges[0], edges[1], edges[highI], pg[0]);
            InitEdge(edges[highI], edges[0], edges[highI - 1], pg[highI]);
            for (int i = highI - 1; i >= 1; --i)
            {
                RangeTest(pg[i], ref MUseFullRange);
                InitEdge(edges[i], edges[i + 1], edges[i - 1], pg[i]);
            }
            var eStart = edges[0];

            //2. Remove duplicate vertices, and (when closed) collinear edges ...
            TEdge edge = eStart, eLoopStop = eStart;
            for (;;)
            {
                //nb: allows matching start and end points when not Closed ...
                if (edge.Curr == edge.Next.Curr && (Closed || edge.Next != eStart))
                {
                    if (edge == edge.Next) break;
                    if (edge == eStart) eStart = edge.Next;
                    edge = RemoveEdge(edge);
                    eLoopStop = edge;
                    continue;
                }
                if (edge.Prev == edge.Next)
                    break; //only two vertices
                else if (Closed &&
                  SlopesEqual(edge.Prev.Curr, edge.Curr, edge.Next.Curr, MUseFullRange) &&
                  (!PreserveCollinear ||
                  !Pt2IsBetweenPt1AndPt3(edge.Prev.Curr, edge.Curr, edge.Next.Curr)))
                {
                    //Collinear edges are allowed for open paths but in closed paths
                    //the default is to merge adjacent collinear edges into a single edge.
                    //However, if the PreserveCollinear property is enabled, only overlapping
                    //collinear edges (ie spikes) will be removed from closed paths.
                    if (edge == eStart) eStart = edge.Next;
                    edge = RemoveEdge(edge);
                    edge = edge.Prev;
                    eLoopStop = edge;
                    continue;
                }
                edge = edge.Next;
                if ((edge == eLoopStop) || (!Closed && edge.Next == eStart)) break;
            }

            if ((!Closed && (edge == edge.Next)) || (Closed && (edge.Prev == edge.Next)))
                return false;

            if (!Closed)
            {
                MHasOpenPaths = true;
                eStart.Prev.OutIdx = Skip;
            }

            //3. Do second stage of edge initialization ...
            edge = eStart;
            do
            {
                InitEdge2(edge, polyType);
                edge = edge.Next;
                if (isFlat && edge.Curr.Y != eStart.Curr.Y) isFlat = false;
            }
            while (edge != eStart);

            //4. Finally, add edge bounds to LocalMinima list ...

            //Totally flat paths must be handled differently when adding them
            //to LocalMinima list to avoid endless loops etc ...
            if (isFlat)
            {
                if (Closed) return false;
                edge.Prev.OutIdx = Skip;
                if (edge.Prev.Bot.X < edge.Prev.Top.X) ReverseHorizontal(edge.Prev);
                LocalMinima locMin = new LocalMinima();
                locMin.Next = null;
                locMin.Y = edge.Bot.Y;
                locMin.LeftBound = null;
                locMin.RightBound = edge;
                locMin.RightBound.Side = EdgeSide.Right;
                locMin.RightBound.WindDelta = 0;
                while (edge.Next.OutIdx != Skip)
                {
                    edge.NextInLML = edge.Next;
                    if (edge.Bot.X != edge.Prev.Top.X) ReverseHorizontal(edge);
                    edge = edge.Next;
                }
                InsertLocalMinima(locMin);
                MEdges.Add(edges);
                return true;
            }

            MEdges.Add(edges);
            bool leftBoundIsForward;
            TEdge EMin = null;

            //workaround to avoid an endless loop in the while loop below when
            //open paths have matching start and end points ...
            if (edge.Prev.Bot == edge.Prev.Top) edge = edge.Next;

            for (;;)
            {
                edge = FindNextLocMin(edge);
                if (edge == EMin) break;
                if (EMin == null) EMin = edge;

                //E and E.Prev now share a local minima (left aligned if horizontal).
                //Compare their slopes to find which starts which bound ...
                var locMin = new LocalMinima
                {
                    Next = null,
                    Y = edge.Bot.Y
                };
                if (edge.Dx < edge.Prev.Dx)
                {
                    locMin.LeftBound = edge.Prev;
                    locMin.RightBound = edge;
                    leftBoundIsForward = false; //Q.nextInLML = Q.prev
                }
                else
                {
                    locMin.LeftBound = edge;
                    locMin.RightBound = edge.Prev;
                    leftBoundIsForward = true; //Q.nextInLML = Q.next
                }
                locMin.LeftBound.Side = EdgeSide.Left;
                locMin.RightBound.Side = EdgeSide.Right;

                if (!Closed) locMin.LeftBound.WindDelta = 0;
                else if (locMin.LeftBound.Next == locMin.RightBound)
                    locMin.LeftBound.WindDelta = -1;
                else locMin.LeftBound.WindDelta = 1;
                locMin.RightBound.WindDelta = -locMin.LeftBound.WindDelta;

                edge = ProcessBound(locMin.LeftBound, leftBoundIsForward);
                if (edge.OutIdx == Skip) edge = ProcessBound(edge, leftBoundIsForward);

                var edge2 = ProcessBound(locMin.RightBound, !leftBoundIsForward);
                if (edge2.OutIdx == Skip) edge2 = ProcessBound(edge2, !leftBoundIsForward);

                if (locMin.LeftBound.OutIdx == Skip)
                    locMin.LeftBound = null;
                else if (locMin.RightBound.OutIdx == Skip)
                    locMin.RightBound = null;
                InsertLocalMinima(locMin);
                if (!leftBoundIsForward) edge = edge2;
            }
            return true;

        }
        //------------------------------------------------------------------------------

        internal bool AddPaths(Paths ppg, PolyType polyType, bool closed)
        {
            var result = false;
            for (var i = 0; i < ppg.Count; ++i)
                if (AddPath(ppg[i], polyType, closed)) result = true;
            return result;
        }
        //------------------------------------------------------------------------------

        internal bool Pt2IsBetweenPt1AndPt3(IntPoint pt1, IntPoint pt2, IntPoint pt3)
        {
            if ((pt1 == pt3) || (pt1 == pt2) || (pt3 == pt2)) return false;
            if (pt1.X != pt3.X) return (pt2.X > pt1.X) == (pt2.X < pt3.X);
            return (pt2.Y > pt1.Y) == (pt2.Y < pt3.Y);
        }
        //------------------------------------------------------------------------------

        static TEdge RemoveEdge(TEdge e)
        {
            //removes e from double_linked_list (but without removing from memory)
            e.Prev.Next = e.Next;
            e.Next.Prev = e.Prev;
            var result = e.Next;
            e.Prev = null; //flag as removed (see ClipperBase.Clear)
            return result;
        }
        //------------------------------------------------------------------------------

        private static void SetDx(TEdge e)
        {
            e.Delta.X = (e.Top.X - e.Bot.X);
            e.Delta.Y = (e.Top.Y - e.Bot.Y);
            if (e.Delta.Y == 0) e.Dx = Horizontal;
            else e.Dx = (double)(e.Delta.X) / (e.Delta.Y);
        }
        //---------------------------------------------------------------------------

        private void InsertLocalMinima(LocalMinima newLm)
        {
            if (MMinimaList == null)
            {
                MMinimaList = newLm;
            }
            else if (newLm.Y >= MMinimaList.Y)
            {
                newLm.Next = MMinimaList;
                MMinimaList = newLm;
            }
            else
            {
                var tmpLm = MMinimaList;
                while (tmpLm.Next != null && (newLm.Y < tmpLm.Next.Y))
                    tmpLm = tmpLm.Next;
                newLm.Next = tmpLm.Next;
                tmpLm.Next = newLm;
            }
        }
        //------------------------------------------------------------------------------

        protected void PopLocalMinima()
        {
            MCurrentLm = MCurrentLm?.Next;
        }
        //------------------------------------------------------------------------------

        private void ReverseHorizontal(TEdge e)
        {
            //swap horizontal edges' top and bottom x's so they follow the natural
            //progression of the bounds - ie so their xbots will align with the
            //adjoining lower edge. [Helpful in the ProcessHorizontal() method.]
            Swap(ref e.Top.X, ref e.Bot.X);
        }
        //------------------------------------------------------------------------------

        protected virtual void Reset()
        {
            MCurrentLm = MMinimaList;
            if (MCurrentLm == null) return; //ie nothing to process

            //reset all edges ...
            var localMinima = MMinimaList;
            while (localMinima != null)
            {
                var edge = localMinima.LeftBound;
                if (edge != null)
                {
                    edge.Curr = edge.Bot;
                    edge.Side = EdgeSide.Left;
                    edge.OutIdx = Unassigned;
                }
                edge = localMinima.RightBound;
                if (edge != null)
                {
                    edge.Curr = edge.Bot;
                    edge.Side = EdgeSide.Right;
                    edge.OutIdx = Unassigned;
                }
                localMinima = localMinima.Next;
            }
        }
        //------------------------------------------------------------------------------

        internal static IntRect GetBounds(Paths paths)
        {
            int i = 0, cnt = paths.Count;
            while (i < cnt && paths[i].Count == 0) i++;
            if (i == cnt) return new IntRect(0, 0, 0, 0);
            var result = new IntRect {left = paths[i][0].X};
            result.right = result.left;
            result.top = paths[i][0].Y;
            result.bottom = result.top;
            for (; i < cnt; i++)
                for (int j = 0; j < paths[i].Count; j++)
                {
                    if (paths[i][j].X < result.left) result.left = paths[i][j].X;
                    else if (paths[i][j].X > result.right) result.right = paths[i][j].X;
                    if (paths[i][j].Y < result.top) result.top = paths[i][j].Y;
                    else if (paths[i][j].Y > result.bottom) result.bottom = paths[i][j].Y;
                }
            return result;
        }

    } //end ClipperBase
    #endregion

    #region Clipper Class
    internal class Clipper : ClipperBase
    {
        //InitOptions that can be passed to the constructor ...
        internal const int IOReverseSolution = 1;
        internal const int IOStrictlySimple = 2;
        internal const int IOPreserveCollinear = 4;

        private List<OutRec> m_PolyOuts;
        private ClipType m_ClipType;
        private Scanbeam m_Scanbeam;
        private TEdge m_ActiveEdges;
        private TEdge m_SortedEdges;
        private List<IntersectNode> m_IntersectList;
        IComparer<IntersectNode> m_IntersectNodeComparer;
        private bool m_ExecuteLocked;
        private PolyFillType m_ClipFillType;
        private PolyFillType m_SubjFillType;
        private List<Join> m_Joins;
        private List<Join> m_GhostJoins;
        private bool m_UsingPolyTree;

        internal Clipper(int initOptions = 0) //constructor
        {
            m_Scanbeam = null;
            m_ActiveEdges = null;
            m_SortedEdges = null;
            m_IntersectList = new List<IntersectNode>();
            m_IntersectNodeComparer = new MyIntersectNodeSort();
            m_ExecuteLocked = false;
            m_UsingPolyTree = false;
            m_PolyOuts = new List<OutRec>();
            m_Joins = new List<Join>();
            m_GhostJoins = new List<Join>();
            ReverseSolution = (IOReverseSolution & initOptions) != 0;
            StrictlySimple = (IOStrictlySimple & initOptions) != 0;
            PreserveCollinear = (IOPreserveCollinear & initOptions) != 0;
        }
        //------------------------------------------------------------------------------

        protected override void Reset()
        {
            base.Reset();
            m_Scanbeam = null;
            m_ActiveEdges = null;
            m_SortedEdges = null;
            var lm = MMinimaList;
            while (lm != null)
            {
                InsertScanbeam(lm.Y);
                lm = lm.Next;
            }
        }
        //------------------------------------------------------------------------------

        internal bool ReverseSolution
        {
            get;
            set;
        }
        //------------------------------------------------------------------------------

        internal bool StrictlySimple
        {
            get;
            set;
        }
        //------------------------------------------------------------------------------

        private void InsertScanbeam(long Y)
        {
            if (m_Scanbeam == null)
            {
                m_Scanbeam = new Scanbeam
                {
                    Next = null,
                    Y = Y
                };
            }
            else if (Y > m_Scanbeam.Y)
            {
                var newSb = new Scanbeam
                {
                    Y = Y,
                    Next = m_Scanbeam
                };
                m_Scanbeam = newSb;
            }
            else
            {
                var sb2 = m_Scanbeam;
                while (sb2.Next != null && (Y <= sb2.Next.Y)) sb2 = sb2.Next;
                if (Y == sb2.Y) return; //ie ignores duplicates
                var newSb = new Scanbeam
                {
                    Y = Y,
                    Next = sb2.Next
                };
                sb2.Next = newSb;
            }
        }
        //------------------------------------------------------------------------------

        internal bool Execute(ClipType clipType, Paths solution,
            PolyFillType subjFillType, PolyFillType clipFillType)
        {
            if (m_ExecuteLocked) return false;
            if (MHasOpenPaths) throw
              new ClipperException("Error: PolyTree struct is need for open path clipping.");

            m_ExecuteLocked = true;
            solution.Clear();
            m_SubjFillType = subjFillType;
            m_ClipFillType = clipFillType;
            m_ClipType = clipType;
            m_UsingPolyTree = false;
            bool succeeded;
            try
            {
                succeeded = ExecuteInternal();
                //build the return polygons ...
                if (succeeded) BuildResult(solution);
            }
            finally
            {
                DisposeAllPolyPts();
                m_ExecuteLocked = false;
            }
            return succeeded;
        }
        //------------------------------------------------------------------------------

        internal bool Execute(ClipType clipType, PolyTree polytree,
            PolyFillType subjFillType, PolyFillType clipFillType)
        {
            if (m_ExecuteLocked) return false;
            m_ExecuteLocked = true;
            m_SubjFillType = subjFillType;
            m_ClipFillType = clipFillType;
            m_ClipType = clipType;
            m_UsingPolyTree = true;
            bool succeeded;
            try
            {
                succeeded = ExecuteInternal();
                //build the return polygons ...
                if (succeeded) BuildResult2(polytree);
            }
            finally
            {
                DisposeAllPolyPts();
                m_ExecuteLocked = false;
            }
            return succeeded;
        }
        //------------------------------------------------------------------------------

        internal bool Execute(ClipType clipType, Paths solution)
        {
            return Execute(clipType, solution,
                PolyFillType.EvenOdd, PolyFillType.EvenOdd);
        }
        //------------------------------------------------------------------------------

        internal bool Execute(ClipType clipType, PolyTree polytree)
        {
            return Execute(clipType, polytree,
                PolyFillType.EvenOdd, PolyFillType.EvenOdd);
        }
        //------------------------------------------------------------------------------

        internal void FixHoleLinkage(OutRec outRec)
        {
            //skip if an outermost polygon or
            //already already points to the correct FirstLeft ...
            if (outRec.FirstLeft == null ||
                  (outRec.IsHole != outRec.FirstLeft.IsHole &&
                  outRec.FirstLeft.Pts != null)) return;

            var orfl = outRec.FirstLeft;
            while (orfl != null && ((orfl.IsHole == outRec.IsHole) || orfl.Pts == null))
                orfl = orfl.FirstLeft;
            outRec.FirstLeft = orfl;
        }
        //------------------------------------------------------------------------------

        private bool ExecuteInternal()
        {
            try
            {
                Reset();
                if (MCurrentLm == null) return false;

                var botY = PopScanbeam();
                do
                {
                    InsertLocalMinimaIntoAEL(botY);
                    m_GhostJoins.Clear();
                    ProcessHorizontals(false);
                    if (m_Scanbeam == null) break;
                    var topY = PopScanbeam();
                    if (!ProcessIntersections(topY)) return false;
                    ProcessEdgesAtTopOfScanbeam(topY);
                    botY = topY;
                } while (m_Scanbeam != null || MCurrentLm != null);

                //fix orientations ...
                foreach (var outRec in m_PolyOuts)
                {
                    if (outRec.Pts == null || outRec.IsOpen) continue;
                    if ((outRec.IsHole ^ ReverseSolution) == (Area(outRec) > 0))
                        ReversePolyPtLinks(outRec.Pts);
                }

                JoinCommonEdges();

                foreach (OutRec outRec in m_PolyOuts)
                {
                    if (outRec.Pts != null && !outRec.IsOpen)
                        FixupOutPolygon(outRec);
                }

                if (StrictlySimple) DoSimplePolygons();
                return true;
            }
            //catch { return false; }
            finally
            {
                m_Joins.Clear();
                m_GhostJoins.Clear();
            }
        }
        //------------------------------------------------------------------------------

        private long PopScanbeam()
        {
            var y = m_Scanbeam.Y;
            m_Scanbeam = m_Scanbeam.Next;
            return y;
        }
        //------------------------------------------------------------------------------

        private void DisposeAllPolyPts()
        {
            for (var i = 0; i < m_PolyOuts.Count; ++i) DisposeOutRec(i);
            m_PolyOuts.Clear();
        }
        //------------------------------------------------------------------------------

        private void DisposeOutRec(int index)
        {
            var outRec = m_PolyOuts[index];
            outRec.Pts = null;
            m_PolyOuts[index] = null;
        }
        //------------------------------------------------------------------------------

        private void AddJoin(OutPt outPoint1, OutPt outPoint2, IntPoint offPt)
        {
            var join = new Join();
            join.OutPt1 = outPoint1;
            join.OutPt2 = outPoint2;
            join.OffPt = offPt;
            m_Joins.Add(join);
        }
        //------------------------------------------------------------------------------

        private void AddGhostJoin(OutPt outPoint, IntPoint offPt)
        {
            var join = new Join();
            join.OutPt1 = outPoint;
            join.OffPt = offPt;
            m_GhostJoins.Add(join);
        }
        //------------------------------------------------------------------------------

#if use_xyz
      internal void SetZ(ref IntPoint pt, TEdge e1, TEdge e2)
      {
        if (pt.Z != 0 || ZFillFunction == null) return;
        else if (pt == e1.Bot) pt.Z = e1.Bot.Z;
        else if (pt == e1.Top) pt.Z = e1.Top.Z;
        else if (pt == e2.Bot) pt.Z = e2.Bot.Z;
        else if (pt == e2.Top) pt.Z = e2.Top.Z;
        else ZFillFunction(e1.Bot, e1.Top, e2.Bot, e2.Top, ref pt);
      }
      //------------------------------------------------------------------------------
#endif

        private void InsertLocalMinimaIntoAEL(long botY)
        {
            while (MCurrentLm != null && (MCurrentLm.Y == botY))
            {
                var leftBoundEdge = MCurrentLm.LeftBound;
                var rightBoundEdge = MCurrentLm.RightBound;
                PopLocalMinima();

                OutPt outPoint = null;
                if (leftBoundEdge == null)
                {
                    InsertEdgeIntoAEL(rightBoundEdge, null);
                    SetWindingCount(rightBoundEdge);
                    if (IsContributing(rightBoundEdge))
                        outPoint = AddOutPt(rightBoundEdge, rightBoundEdge.Bot);
                }
                else if (rightBoundEdge == null)
                {
                    InsertEdgeIntoAEL(leftBoundEdge, null);
                    SetWindingCount(leftBoundEdge);
                    if (IsContributing(leftBoundEdge))
                        outPoint = AddOutPt(leftBoundEdge, leftBoundEdge.Bot);
                    InsertScanbeam(leftBoundEdge.Top.Y);
                }
                else
                {
                    InsertEdgeIntoAEL(leftBoundEdge, null);
                    InsertEdgeIntoAEL(rightBoundEdge, leftBoundEdge);
                    SetWindingCount(leftBoundEdge);
                    rightBoundEdge.WindCnt = leftBoundEdge.WindCnt;
                    rightBoundEdge.WindCnt2 = leftBoundEdge.WindCnt2;
                    if (IsContributing(leftBoundEdge))
                        outPoint = AddLocalMinPoly(leftBoundEdge, rightBoundEdge, leftBoundEdge.Bot);
                    InsertScanbeam(leftBoundEdge.Top.Y);
                }

                if (rightBoundEdge != null)
                {
                    if (IsHorizontal(rightBoundEdge))
                        AddEdgeToSEL(rightBoundEdge);
                    else
                        InsertScanbeam(rightBoundEdge.Top.Y);
                }

                if (leftBoundEdge == null || rightBoundEdge == null) continue;

                //if output polygons share an Edge with a horizontal rb, they'll need joining later ...
                if (outPoint != null && IsHorizontal(rightBoundEdge) &&
                  m_GhostJoins.Count > 0 && rightBoundEdge.WindDelta != 0)
                {
                    for (var i = 0; i < m_GhostJoins.Count; i++)
                    {
                        //if the horizontal Rb and a 'ghost' horizontal overlap, then convert
                        //the 'ghost' join to a real join ready for later ...
                        var join = m_GhostJoins[i];
                        if (HorzSegmentsOverlap(join.OutPt1.Pt.X, join.OffPt.X, rightBoundEdge.Bot.X, rightBoundEdge.Top.X))
                            AddJoin(join.OutPt1, outPoint, join.OffPt);
                    }
                }

                if (leftBoundEdge.OutIdx >= 0 && leftBoundEdge.PrevInAEL != null &&
                  leftBoundEdge.PrevInAEL.Curr.X == leftBoundEdge.Bot.X &&
                  leftBoundEdge.PrevInAEL.OutIdx >= 0 &&
                  SlopesEqual(leftBoundEdge.PrevInAEL, leftBoundEdge, MUseFullRange) &&
                  leftBoundEdge.WindDelta != 0 && leftBoundEdge.PrevInAEL.WindDelta != 0)
                {
                    var outPoint2 = AddOutPt(leftBoundEdge.PrevInAEL, leftBoundEdge.Bot);
                    AddJoin(outPoint, outPoint2, leftBoundEdge.Top);
                }

                if (leftBoundEdge.NextInAEL == rightBoundEdge) continue;
                if (rightBoundEdge.OutIdx >= 0 && rightBoundEdge.PrevInAEL.OutIdx >= 0 &&
                    SlopesEqual(rightBoundEdge.PrevInAEL, rightBoundEdge, MUseFullRange) &&
                    rightBoundEdge.WindDelta != 0 && rightBoundEdge.PrevInAEL.WindDelta != 0)
                {
                    var outPoint2 = AddOutPt(rightBoundEdge.PrevInAEL, rightBoundEdge.Bot);
                    AddJoin(outPoint, outPoint2, rightBoundEdge.Top);
                }

                var edge = leftBoundEdge.NextInAEL;
                if (edge == null) continue;
                while (edge != rightBoundEdge)
                {
                    //nb: For calculating winding counts etc, IntersectEdges() assumes
                    //that param1 will be to the right of param2 ABOVE the intersection ...
                    IntersectEdges(rightBoundEdge, edge, leftBoundEdge.Curr); //order important here
                    edge = edge.NextInAEL;
                }
            }
        }
        //------------------------------------------------------------------------------

        private void InsertEdgeIntoAEL(TEdge edge, TEdge startEdge)
        {
            if (m_ActiveEdges == null)
            {
                edge.PrevInAEL = null;
                edge.NextInAEL = null;
                m_ActiveEdges = edge;
            }
            else if (startEdge == null && E2InsertsBeforeE1(m_ActiveEdges, edge))
            {
                edge.PrevInAEL = null;
                edge.NextInAEL = m_ActiveEdges;
                m_ActiveEdges.PrevInAEL = edge;
                m_ActiveEdges = edge;
            }
            else
            {
                if (startEdge == null) startEdge = m_ActiveEdges;
                while (startEdge.NextInAEL != null &&
                  !E2InsertsBeforeE1(startEdge.NextInAEL, edge))
                    startEdge = startEdge.NextInAEL;
                edge.NextInAEL = startEdge.NextInAEL;
                if (startEdge.NextInAEL != null) startEdge.NextInAEL.PrevInAEL = edge;
                edge.PrevInAEL = startEdge;
                startEdge.NextInAEL = edge;
            }
        }
        //----------------------------------------------------------------------

        private static bool E2InsertsBeforeE1(TEdge e1, TEdge e2)
        {
            if (e2.Curr.X == e1.Curr.X)
            {
                if (e2.Top.Y > e1.Top.Y)
                    return e2.Top.X < TopX(e1, e2.Top.Y);
                else return e1.Top.X > TopX(e2, e1.Top.Y);
            }
            else return e2.Curr.X < e1.Curr.X;
        }
        //------------------------------------------------------------------------------

        private bool IsEvenOddFillType(TEdge edge)
        {
            if (edge.PolyTyp == PolyType.Subject)
                return m_SubjFillType == PolyFillType.EvenOdd;
            else
                return m_ClipFillType == PolyFillType.EvenOdd;
        }
        //------------------------------------------------------------------------------

        private bool IsEvenOddAltFillType(TEdge edge)
        {
            if (edge.PolyTyp == PolyType.Subject)
                return m_ClipFillType == PolyFillType.EvenOdd;
            else
                return m_SubjFillType == PolyFillType.EvenOdd;
        }
        //------------------------------------------------------------------------------

        private bool IsContributing(TEdge edge)
        {
            PolyFillType pft, pft2;
            if (edge.PolyTyp == PolyType.Subject)
            {
                pft = m_SubjFillType;
                pft2 = m_ClipFillType;
            }
            else
            {
                pft = m_ClipFillType;
                pft2 = m_SubjFillType;
            }

            switch (pft)
            {
                case PolyFillType.EvenOdd:
                    //return false if a subj line has been flagged as inside a subj polygon
                    if (edge.WindDelta == 0 && edge.WindCnt != 1) return false;
                    break;
                case PolyFillType.NonZero:
                    if (Math.Abs(edge.WindCnt) != 1) return false;
                    break;
                case PolyFillType.Positive:
                    if (edge.WindCnt != 1) return false;
                    break;
                default: //PolyFillType.pftNegative
                    if (edge.WindCnt != -1) return false;
                    break;
            }

            switch (m_ClipType)
            {
                case ClipType.Intersection:
                    switch (pft2)
                    {
                        case PolyFillType.EvenOdd:
                        case PolyFillType.NonZero:
                            return (edge.WindCnt2 != 0);
                        case PolyFillType.Positive:
                            return (edge.WindCnt2 > 0);
                        default:
                            return (edge.WindCnt2 < 0);
                    }
                case ClipType.Union:
                    switch (pft2)
                    {
                        case PolyFillType.EvenOdd:
                        case PolyFillType.NonZero:
                            return (edge.WindCnt2 == 0);
                        case PolyFillType.Positive:
                            return (edge.WindCnt2 <= 0);
                        default:
                            return (edge.WindCnt2 >= 0);
                    }
                case ClipType.Difference:
                    if (edge.PolyTyp == PolyType.Subject)
                        switch (pft2)
                        {
                            case PolyFillType.EvenOdd:
                            case PolyFillType.NonZero:
                                return (edge.WindCnt2 == 0);
                            case PolyFillType.Positive:
                                return (edge.WindCnt2 <= 0);
                            default:
                                return (edge.WindCnt2 >= 0);
                        }
                    else
                        switch (pft2)
                        {
                            case PolyFillType.EvenOdd:
                            case PolyFillType.NonZero:
                                return (edge.WindCnt2 != 0);
                            case PolyFillType.Positive:
                                return (edge.WindCnt2 > 0);
                            default:
                                return (edge.WindCnt2 < 0);
                        }
                case ClipType.Xor:
                    if (edge.WindDelta == 0) //XOr always contributing unless open
                        switch (pft2)
                        {
                            case PolyFillType.EvenOdd:
                            case PolyFillType.NonZero:
                                return (edge.WindCnt2 == 0);
                            case PolyFillType.Positive:
                                return (edge.WindCnt2 <= 0);
                            default:
                                return (edge.WindCnt2 >= 0);
                        }
                    else
                        return true;
            }
            return true;
        }
        //------------------------------------------------------------------------------

        private void SetWindingCount(TEdge edge)
        {
            var previousEdge = edge.PrevInAEL;
            //find the edge of the same polytype that immediately preceeds 'edge' in AEL
            while (previousEdge != null && ((previousEdge.PolyTyp != edge.PolyTyp) || (previousEdge.WindDelta == 0))) previousEdge = previousEdge.PrevInAEL;
            if (previousEdge == null)
            {
                edge.WindCnt = (edge.WindDelta == 0 ? 1 : edge.WindDelta);
                edge.WindCnt2 = 0;
                previousEdge = m_ActiveEdges; //ie get ready to calc WindCnt2
            }
            else if (edge.WindDelta == 0 && m_ClipType != ClipType.Union)
            {
                edge.WindCnt = 1;
                edge.WindCnt2 = previousEdge.WindCnt2;
                previousEdge = previousEdge.NextInAEL; //ie get ready to calc WindCnt2
            }
            else if (IsEvenOddFillType(edge))
            {
                //EvenOdd filling ...
                if (edge.WindDelta == 0)
                {
                    //are we inside a subj polygon ...
                    var inside = true;
                    var edge2 = previousEdge.PrevInAEL;
                    while (edge2 != null)
                    {
                        if (edge2.PolyTyp == previousEdge.PolyTyp && edge2.WindDelta != 0)
                            inside = !inside;
                        edge2 = edge2.PrevInAEL;
                    }
                    edge.WindCnt = (inside ? 0 : 1);
                }
                else
                {
                    edge.WindCnt = edge.WindDelta;
                }
                edge.WindCnt2 = previousEdge.WindCnt2;
                previousEdge = previousEdge.NextInAEL; //ie get ready to calc WindCnt2
            }
            else
            {
                //nonZero, Positive or Negative filling ...
                if (previousEdge.WindCnt * previousEdge.WindDelta < 0)
                {
                    //prev edge is 'decreasing' WindCount (WC) toward zero
                    //so we're outside the previous polygon ...
                    if (Math.Abs(previousEdge.WindCnt) > 1)
                    {
                        //outside prev poly but still inside another.
                        //when reversing direction of prev poly use the same WC 
                        if (previousEdge.WindDelta * edge.WindDelta < 0) edge.WindCnt = previousEdge.WindCnt;
                        //otherwise continue to 'decrease' WC ...
                        else edge.WindCnt = previousEdge.WindCnt + edge.WindDelta;
                    }
                    else
                        //now outside all polys of same polytype so set own WC ...
                        edge.WindCnt = (edge.WindDelta == 0 ? 1 : edge.WindDelta);
                }
                else
                {
                    //prev edge is 'increasing' WindCount (WC) away from zero
                    //so we're inside the previous polygon ...
                    if (edge.WindDelta == 0)
                        edge.WindCnt = (previousEdge.WindCnt < 0 ? previousEdge.WindCnt - 1 : previousEdge.WindCnt + 1);
                    //if wind direction is reversing prev then use same WC
                    else if (previousEdge.WindDelta * edge.WindDelta < 0)
                        edge.WindCnt = previousEdge.WindCnt;
                    //otherwise add to WC ...
                    else edge.WindCnt = previousEdge.WindCnt + edge.WindDelta;
                }
                edge.WindCnt2 = previousEdge.WindCnt2;
                previousEdge = previousEdge.NextInAEL; //ie get ready to calc WindCnt2
            }

            //update WindCnt2 ...
            if (IsEvenOddAltFillType(edge))
            {
                //EvenOdd filling ...
                while (previousEdge != edge)
                {
                    if (previousEdge.WindDelta != 0)
                        edge.WindCnt2 = (edge.WindCnt2 == 0 ? 1 : 0);
                    previousEdge = previousEdge.NextInAEL;
                }
            }
            else
            {
                //nonZero, Positive or Negative filling ...
                while (previousEdge != edge)
                {
                    edge.WindCnt2 += previousEdge.WindDelta;
                    previousEdge = previousEdge.NextInAEL;
                }
            }
        }
        //------------------------------------------------------------------------------

        private void AddEdgeToSEL(TEdge edge)
        {
            //SEL pointers in PEdge are reused to build a list of horizontal edges.
            //However, we don't need to worry about order with horizontal edge processing.
            if (m_SortedEdges == null)
            {
                m_SortedEdges = edge;
                edge.PrevInSEL = null;
                edge.NextInSEL = null;
            }
            else
            {
                edge.NextInSEL = m_SortedEdges;
                edge.PrevInSEL = null;
                m_SortedEdges.PrevInSEL = edge;
                m_SortedEdges = edge;
            }
        }
        //------------------------------------------------------------------------------

        private void CopyAELToSEL()
        {
            TEdge e = m_ActiveEdges;
            m_SortedEdges = e;
            while (e != null)
            {
                e.PrevInSEL = e.PrevInAEL;
                e.NextInSEL = e.NextInAEL;
                e = e.NextInAEL;
            }
        }
        //------------------------------------------------------------------------------

        private void SwapPositionsInAEL(TEdge edge1, TEdge edge2)
        {
            //check that one or other edge hasn't already been removed from AEL ...
            if (edge1.NextInAEL == edge1.PrevInAEL ||
              edge2.NextInAEL == edge2.PrevInAEL) return;

            if (edge1.NextInAEL == edge2)
            {
                TEdge next = edge2.NextInAEL;
                if (next != null)
                    next.PrevInAEL = edge1;
                TEdge prev = edge1.PrevInAEL;
                if (prev != null)
                    prev.NextInAEL = edge2;
                edge2.PrevInAEL = prev;
                edge2.NextInAEL = edge1;
                edge1.PrevInAEL = edge2;
                edge1.NextInAEL = next;
            }
            else if (edge2.NextInAEL == edge1)
            {
                TEdge next = edge1.NextInAEL;
                if (next != null)
                    next.PrevInAEL = edge2;
                TEdge prev = edge2.PrevInAEL;
                if (prev != null)
                    prev.NextInAEL = edge1;
                edge1.PrevInAEL = prev;
                edge1.NextInAEL = edge2;
                edge2.PrevInAEL = edge1;
                edge2.NextInAEL = next;
            }
            else
            {
                TEdge next = edge1.NextInAEL;
                TEdge prev = edge1.PrevInAEL;
                edge1.NextInAEL = edge2.NextInAEL;
                if (edge1.NextInAEL != null)
                    edge1.NextInAEL.PrevInAEL = edge1;
                edge1.PrevInAEL = edge2.PrevInAEL;
                if (edge1.PrevInAEL != null)
                    edge1.PrevInAEL.NextInAEL = edge1;
                edge2.NextInAEL = next;
                if (edge2.NextInAEL != null)
                    edge2.NextInAEL.PrevInAEL = edge2;
                edge2.PrevInAEL = prev;
                if (edge2.PrevInAEL != null)
                    edge2.PrevInAEL.NextInAEL = edge2;
            }

            if (edge1.PrevInAEL == null)
                m_ActiveEdges = edge1;
            else if (edge2.PrevInAEL == null)
                m_ActiveEdges = edge2;
        }
        //------------------------------------------------------------------------------

        private void SwapPositionsInSEL(TEdge edge1, TEdge edge2)
        {
            if (edge1.NextInSEL == null && edge1.PrevInSEL == null)
                return;
            if (edge2.NextInSEL == null && edge2.PrevInSEL == null)
                return;

            if (edge1.NextInSEL == edge2)
            {
                TEdge next = edge2.NextInSEL;
                if (next != null)
                    next.PrevInSEL = edge1;
                TEdge prev = edge1.PrevInSEL;
                if (prev != null)
                    prev.NextInSEL = edge2;
                edge2.PrevInSEL = prev;
                edge2.NextInSEL = edge1;
                edge1.PrevInSEL = edge2;
                edge1.NextInSEL = next;
            }
            else if (edge2.NextInSEL == edge1)
            {
                TEdge next = edge1.NextInSEL;
                if (next != null)
                    next.PrevInSEL = edge2;
                TEdge prev = edge2.PrevInSEL;
                if (prev != null)
                    prev.NextInSEL = edge1;
                edge1.PrevInSEL = prev;
                edge1.NextInSEL = edge2;
                edge2.PrevInSEL = edge1;
                edge2.NextInSEL = next;
            }
            else
            {
                TEdge next = edge1.NextInSEL;
                TEdge prev = edge1.PrevInSEL;
                edge1.NextInSEL = edge2.NextInSEL;
                if (edge1.NextInSEL != null)
                    edge1.NextInSEL.PrevInSEL = edge1;
                edge1.PrevInSEL = edge2.PrevInSEL;
                if (edge1.PrevInSEL != null)
                    edge1.PrevInSEL.NextInSEL = edge1;
                edge2.NextInSEL = next;
                if (edge2.NextInSEL != null)
                    edge2.NextInSEL.PrevInSEL = edge2;
                edge2.PrevInSEL = prev;
                if (edge2.PrevInSEL != null)
                    edge2.PrevInSEL.NextInSEL = edge2;
            }

            if (edge1.PrevInSEL == null)
                m_SortedEdges = edge1;
            else if (edge2.PrevInSEL == null)
                m_SortedEdges = edge2;
        }
        //------------------------------------------------------------------------------


        private void AddLocalMaxPoly(TEdge e1, TEdge e2, IntPoint pt)
        {
            AddOutPt(e1, pt);
            if (e2.WindDelta == 0) AddOutPt(e2, pt);
            if (e1.OutIdx == e2.OutIdx)
            {
                e1.OutIdx = Unassigned;
                e2.OutIdx = Unassigned;
            }
            else if (e1.OutIdx < e2.OutIdx)
                AppendPolygon(e1, e2);
            else
                AppendPolygon(e2, e1);
        }
        //------------------------------------------------------------------------------

        private OutPt AddLocalMinPoly(TEdge e1, TEdge e2, IntPoint pt)
        {
            OutPt result;
            TEdge e, prevE;
            if (IsHorizontal(e2) || (e1.Dx > e2.Dx))
            {
                result = AddOutPt(e1, pt);
                e2.OutIdx = e1.OutIdx;
                e1.Side = EdgeSide.Left;
                e2.Side = EdgeSide.Right;
                e = e1;
                if (e.PrevInAEL == e2)
                    prevE = e2.PrevInAEL;
                else
                    prevE = e.PrevInAEL;
            }
            else
            {
                result = AddOutPt(e2, pt);
                e1.OutIdx = e2.OutIdx;
                e1.Side = EdgeSide.Right;
                e2.Side = EdgeSide.Left;
                e = e2;
                if (e.PrevInAEL == e1)
                    prevE = e1.PrevInAEL;
                else
                    prevE = e.PrevInAEL;
            }

            if (prevE != null && prevE.OutIdx >= 0 &&
                (TopX(prevE, pt.Y) == TopX(e, pt.Y)) &&
                SlopesEqual(e, prevE, MUseFullRange) &&
                (e.WindDelta != 0) && (prevE.WindDelta != 0))
            {
                OutPt outPt = AddOutPt(prevE, pt);
                AddJoin(result, outPt, e.Top);
            }
            return result;
        }
        //------------------------------------------------------------------------------

        private OutRec CreateOutRec()
        {
            OutRec result = new OutRec();
            result.Idx = Unassigned;
            result.IsHole = false;
            result.IsOpen = false;
            result.FirstLeft = null;
            result.Pts = null;
            result.BottomPt = null;
            result.PolyNode = null;
            m_PolyOuts.Add(result);
            result.Idx = m_PolyOuts.Count - 1;
            return result;
        }
        //------------------------------------------------------------------------------

        private OutPt AddOutPt(TEdge e, IntPoint pt)
        {
            bool ToFront = (e.Side == EdgeSide.Left);
            if (e.OutIdx < 0)
            {
                OutRec outRec = CreateOutRec();
                outRec.IsOpen = (e.WindDelta == 0);
                OutPt newOp = new OutPt();
                outRec.Pts = newOp;
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = newOp;
                newOp.Prev = newOp;
                if (!outRec.IsOpen)
                    SetHoleState(e, outRec);
                e.OutIdx = outRec.Idx; //nb: do this after SetZ !
                return newOp;
            }
            else
            {
                OutRec outRec = m_PolyOuts[e.OutIdx];
                //OutRec.Pts is the 'Left-most' point & OutRec.Pts.Prev is the 'Right-most'
                OutPt op = outRec.Pts;
                if (ToFront && pt == op.Pt) return op;
                else if (!ToFront && pt == op.Prev.Pt) return op.Prev;

                OutPt newOp = new OutPt();
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = op;
                newOp.Prev = op.Prev;
                newOp.Prev.Next = newOp;
                op.Prev = newOp;
                if (ToFront) outRec.Pts = newOp;
                return newOp;
            }
        }
        //------------------------------------------------------------------------------

        internal void SwapPoints(ref IntPoint pt1, ref IntPoint pt2)
        {
            IntPoint tmp = new IntPoint(pt1);
            pt1 = pt2;
            pt2 = tmp;
        }
        //------------------------------------------------------------------------------

        private bool HorzSegmentsOverlap(long seg1a, long seg1b, long seg2a, long seg2b)
        {
            if (seg1a > seg1b) Swap(ref seg1a, ref seg1b);
            if (seg2a > seg2b) Swap(ref seg2a, ref seg2b);
            return (seg1a < seg2b) && (seg2a < seg1b);
        }
        //------------------------------------------------------------------------------

        private void SetHoleState(TEdge e, OutRec outRec)
        {
            bool isHole = false;
            TEdge e2 = e.PrevInAEL;
            while (e2 != null)
            {
                if (e2.OutIdx >= 0 && e2.WindDelta != 0)
                {
                    isHole = !isHole;
                    if (outRec.FirstLeft == null)
                        outRec.FirstLeft = m_PolyOuts[e2.OutIdx];
                }
                e2 = e2.PrevInAEL;
            }
            if (isHole)
                outRec.IsHole = true;
        }
        //------------------------------------------------------------------------------

        private double GetDx(IntPoint pt1, IntPoint pt2)
        {
            if (pt1.Y == pt2.Y) return Horizontal;
            else return (double)(pt2.X - pt1.X) / (pt2.Y - pt1.Y);
        }
        //---------------------------------------------------------------------------

        private bool FirstIsBottomPt(OutPt btmPt1, OutPt btmPt2)
        {
            OutPt p = btmPt1.Prev;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1)) p = p.Prev;
            double dx1p = Math.Abs(GetDx(btmPt1.Pt, p.Pt));
            p = btmPt1.Next;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1)) p = p.Next;
            double dx1n = Math.Abs(GetDx(btmPt1.Pt, p.Pt));

            p = btmPt2.Prev;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2)) p = p.Prev;
            double dx2p = Math.Abs(GetDx(btmPt2.Pt, p.Pt));
            p = btmPt2.Next;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2)) p = p.Next;
            double dx2n = Math.Abs(GetDx(btmPt2.Pt, p.Pt));
            return (dx1p >= dx2p && dx1p >= dx2n) || (dx1n >= dx2p && dx1n >= dx2n);
        }
        //------------------------------------------------------------------------------

        private OutPt GetBottomPt(OutPt pp)
        {
            OutPt dups = null;
            OutPt p = pp.Next;
            while (p != pp)
            {
                if (p.Pt.Y > pp.Pt.Y)
                {
                    pp = p;
                    dups = null;
                }
                else if (p.Pt.Y == pp.Pt.Y && p.Pt.X <= pp.Pt.X)
                {
                    if (p.Pt.X < pp.Pt.X)
                    {
                        dups = null;
                        pp = p;
                    }
                    else
                    {
                        if (p.Next != pp && p.Prev != pp) dups = p;
                    }
                }
                p = p.Next;
            }
            if (dups != null)
            {
                //there appears to be at least 2 vertices at bottomPt so ...
                while (dups != p)
                {
                    if (!FirstIsBottomPt(p, dups)) pp = dups;
                    dups = dups.Next;
                    while (dups.Pt != pp.Pt) dups = dups.Next;
                }
            }
            return pp;
        }
        //------------------------------------------------------------------------------

        private OutRec GetLowermostRec(OutRec outRec1, OutRec outRec2)
        {
            //work out which polygon fragment has the correct hole state ...
            if (outRec1.BottomPt == null)
                outRec1.BottomPt = GetBottomPt(outRec1.Pts);
            if (outRec2.BottomPt == null)
                outRec2.BottomPt = GetBottomPt(outRec2.Pts);
            OutPt bPt1 = outRec1.BottomPt;
            OutPt bPt2 = outRec2.BottomPt;
            if (bPt1.Pt.Y > bPt2.Pt.Y) return outRec1;
            else if (bPt1.Pt.Y < bPt2.Pt.Y) return outRec2;
            else if (bPt1.Pt.X < bPt2.Pt.X) return outRec1;
            else if (bPt1.Pt.X > bPt2.Pt.X) return outRec2;
            else if (bPt1.Next == bPt1) return outRec2;
            else if (bPt2.Next == bPt2) return outRec1;
            else if (FirstIsBottomPt(bPt1, bPt2)) return outRec1;
            else return outRec2;
        }
        //------------------------------------------------------------------------------

        bool Param1RightOfParam2(OutRec outRec1, OutRec outRec2)
        {
            do
            {
                outRec1 = outRec1.FirstLeft;
                if (outRec1 == outRec2) return true;
            } while (outRec1 != null);
            return false;
        }
        //------------------------------------------------------------------------------

        private OutRec GetOutRec(int idx)
        {
            OutRec outrec = m_PolyOuts[idx];
            while (outrec != m_PolyOuts[outrec.Idx])
                outrec = m_PolyOuts[outrec.Idx];
            return outrec;
        }
        //------------------------------------------------------------------------------

        private void AppendPolygon(TEdge e1, TEdge e2)
        {
            //get the start and ends of both output polygons ...
            OutRec outRec1 = m_PolyOuts[e1.OutIdx];
            OutRec outRec2 = m_PolyOuts[e2.OutIdx];

            OutRec holeStateRec;
            if (Param1RightOfParam2(outRec1, outRec2))
                holeStateRec = outRec2;
            else if (Param1RightOfParam2(outRec2, outRec1))
                holeStateRec = outRec1;
            else
                holeStateRec = GetLowermostRec(outRec1, outRec2);

            OutPt p1_lft = outRec1.Pts;
            OutPt p1_rt = p1_lft.Prev;
            OutPt p2_lft = outRec2.Pts;
            OutPt p2_rt = p2_lft.Prev;

            EdgeSide side;
            //join e2 poly onto e1 poly and delete pointers to e2 ...
            if (e1.Side == EdgeSide.Left)
            {
                if (e2.Side == EdgeSide.Left)
                {
                    //z y x a b c
                    ReversePolyPtLinks(p2_lft);
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    outRec1.Pts = p2_rt;
                }
                else
                {
                    //x y z a b c
                    p2_rt.Next = p1_lft;
                    p1_lft.Prev = p2_rt;
                    p2_lft.Prev = p1_rt;
                    p1_rt.Next = p2_lft;
                    outRec1.Pts = p2_lft;
                }
                side = EdgeSide.Left;
            }
            else
            {
                if (e2.Side == EdgeSide.Right)
                {
                    //a b c z y x
                    ReversePolyPtLinks(p2_lft);
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                }
                else
                {
                    //a b c x y z
                    p1_rt.Next = p2_lft;
                    p2_lft.Prev = p1_rt;
                    p1_lft.Prev = p2_rt;
                    p2_rt.Next = p1_lft;
                }
                side = EdgeSide.Right;
            }

            outRec1.BottomPt = null;
            if (holeStateRec == outRec2)
            {
                if (outRec2.FirstLeft != outRec1)
                    outRec1.FirstLeft = outRec2.FirstLeft;
                outRec1.IsHole = outRec2.IsHole;
            }
            outRec2.Pts = null;
            outRec2.BottomPt = null;

            outRec2.FirstLeft = outRec1;

            int OKIdx = e1.OutIdx;
            int ObsoleteIdx = e2.OutIdx;

            e1.OutIdx = Unassigned; //nb: safe because we only get here via AddLocalMaxPoly
            e2.OutIdx = Unassigned;

            TEdge e = m_ActiveEdges;
            while (e != null)
            {
                if (e.OutIdx == ObsoleteIdx)
                {
                    e.OutIdx = OKIdx;
                    e.Side = side;
                    break;
                }
                e = e.NextInAEL;
            }
            outRec2.Idx = outRec1.Idx;
        }
        //------------------------------------------------------------------------------

        private void ReversePolyPtLinks(OutPt pp)
        {
            if (pp == null) return;
            OutPt pp1;
            OutPt pp2;
            pp1 = pp;
            do
            {
                pp2 = pp1.Next;
                pp1.Next = pp1.Prev;
                pp1.Prev = pp2;
                pp1 = pp2;
            } while (pp1 != pp);
        }
        //------------------------------------------------------------------------------

        private static void SwapSides(TEdge edge1, TEdge edge2)
        {
            EdgeSide side = edge1.Side;
            edge1.Side = edge2.Side;
            edge2.Side = side;
        }
        //------------------------------------------------------------------------------

        private static void SwapPolyIndexes(TEdge edge1, TEdge edge2)
        {
            int outIdx = edge1.OutIdx;
            edge1.OutIdx = edge2.OutIdx;
            edge2.OutIdx = outIdx;
        }
        //------------------------------------------------------------------------------

        private void IntersectEdges(TEdge e1, TEdge e2, IntPoint pt)
        {
            //e1 will be to the left of e2 BELOW the intersection. Therefore e1 is before
            //e2 in AEL except when e1 is being inserted at the intersection point ...

            bool e1Contributing = (e1.OutIdx >= 0);
            bool e2Contributing = (e2.OutIdx >= 0);

#if use_xyz
          SetZ(ref pt, e1, e2);
#endif

            //if either edge is on an OPEN path ...
            if (e1.WindDelta == 0 || e2.WindDelta == 0)
            {
                //ignore subject-subject open path intersections UNLESS they
                //are both open paths, AND they are both 'contributing maximas' ...
                if (e1.WindDelta == 0 && e2.WindDelta == 0) return;
                //if intersecting a subj line with a subj poly ...
                else if (e1.PolyTyp == e2.PolyTyp &&
                  e1.WindDelta != e2.WindDelta && m_ClipType == ClipType.Union)
                {
                    if (e1.WindDelta == 0)
                    {
                        if (e2Contributing)
                        {
                            AddOutPt(e1, pt);
                            if (e1Contributing) e1.OutIdx = Unassigned;
                        }
                    }
                    else
                    {
                        if (e1Contributing)
                        {
                            AddOutPt(e2, pt);
                            if (e2Contributing) e2.OutIdx = Unassigned;
                        }
                    }
                }
                else if (e1.PolyTyp != e2.PolyTyp)
                {
                    if ((e1.WindDelta == 0) && Math.Abs(e2.WindCnt) == 1 &&
                      (m_ClipType != ClipType.Union || e2.WindCnt2 == 0))
                    {
                        AddOutPt(e1, pt);
                        if (e1Contributing) e1.OutIdx = Unassigned;
                    }
                    else if ((e2.WindDelta == 0) && (Math.Abs(e1.WindCnt) == 1) &&
                      (m_ClipType != ClipType.Union || e1.WindCnt2 == 0))
                    {
                        AddOutPt(e2, pt);
                        if (e2Contributing) e2.OutIdx = Unassigned;
                    }
                }
                return;
            }

            //update winding counts...
            //assumes that e1 will be to the Right of e2 ABOVE the intersection
            if (e1.PolyTyp == e2.PolyTyp)
            {
                if (IsEvenOddFillType(e1))
                {
                    int oldE1WindCnt = e1.WindCnt;
                    e1.WindCnt = e2.WindCnt;
                    e2.WindCnt = oldE1WindCnt;
                }
                else
                {
                    if (e1.WindCnt + e2.WindDelta == 0) e1.WindCnt = -e1.WindCnt;
                    else e1.WindCnt += e2.WindDelta;
                    if (e2.WindCnt - e1.WindDelta == 0) e2.WindCnt = -e2.WindCnt;
                    else e2.WindCnt -= e1.WindDelta;
                }
            }
            else
            {
                if (!IsEvenOddFillType(e2)) e1.WindCnt2 += e2.WindDelta;
                else e1.WindCnt2 = (e1.WindCnt2 == 0) ? 1 : 0;
                if (!IsEvenOddFillType(e1)) e2.WindCnt2 -= e1.WindDelta;
                else e2.WindCnt2 = (e2.WindCnt2 == 0) ? 1 : 0;
            }

            PolyFillType e1FillType, e2FillType, e1FillType2, e2FillType2;
            if (e1.PolyTyp == PolyType.Subject)
            {
                e1FillType = m_SubjFillType;
                e1FillType2 = m_ClipFillType;
            }
            else
            {
                e1FillType = m_ClipFillType;
                e1FillType2 = m_SubjFillType;
            }
            if (e2.PolyTyp == PolyType.Subject)
            {
                e2FillType = m_SubjFillType;
                e2FillType2 = m_ClipFillType;
            }
            else
            {
                e2FillType = m_ClipFillType;
                e2FillType2 = m_SubjFillType;
            }

            int e1Wc, e2Wc;
            switch (e1FillType)
            {
                case PolyFillType.Positive: e1Wc = e1.WindCnt; break;
                case PolyFillType.Negative: e1Wc = -e1.WindCnt; break;
                default: e1Wc = Math.Abs(e1.WindCnt); break;
            }
            switch (e2FillType)
            {
                case PolyFillType.Positive: e2Wc = e2.WindCnt; break;
                case PolyFillType.Negative: e2Wc = -e2.WindCnt; break;
                default: e2Wc = Math.Abs(e2.WindCnt); break;
            }

            if (e1Contributing && e2Contributing)
            {
                if ((e1Wc != 0 && e1Wc != 1) || (e2Wc != 0 && e2Wc != 1) ||
                  (e1.PolyTyp != e2.PolyTyp && m_ClipType != ClipType.Xor))
                {
                    AddLocalMaxPoly(e1, e2, pt);
                }
                else
                {
                    AddOutPt(e1, pt);
                    AddOutPt(e2, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if (e1Contributing)
            {
                if (e2Wc == 0 || e2Wc == 1)
                {
                    AddOutPt(e1, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }

            }
            else if (e2Contributing)
            {
                if (e1Wc == 0 || e1Wc == 1)
                {
                    AddOutPt(e2, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if ((e1Wc == 0 || e1Wc == 1) && (e2Wc == 0 || e2Wc == 1))
            {
                //neither edge is currently contributing ...
                long e1Wc2, e2Wc2;
                switch (e1FillType2)
                {
                    case PolyFillType.Positive: e1Wc2 = e1.WindCnt2; break;
                    case PolyFillType.Negative: e1Wc2 = -e1.WindCnt2; break;
                    default: e1Wc2 = Math.Abs(e1.WindCnt2); break;
                }
                switch (e2FillType2)
                {
                    case PolyFillType.Positive: e2Wc2 = e2.WindCnt2; break;
                    case PolyFillType.Negative: e2Wc2 = -e2.WindCnt2; break;
                    default: e2Wc2 = Math.Abs(e2.WindCnt2); break;
                }

                if (e1.PolyTyp != e2.PolyTyp)
                {
                    AddLocalMinPoly(e1, e2, pt);
                }
                else if (e1Wc == 1 && e2Wc == 1)
                    switch (m_ClipType)
                    {
                        case ClipType.Intersection:
                            if (e1Wc2 > 0 && e2Wc2 > 0)
                                AddLocalMinPoly(e1, e2, pt);
                            break;
                        case ClipType.Union:
                            if (e1Wc2 <= 0 && e2Wc2 <= 0)
                                AddLocalMinPoly(e1, e2, pt);
                            break;
                        case ClipType.Difference:
                            if (((e1.PolyTyp == PolyType.Clip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                                ((e1.PolyTyp == PolyType.Subject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
                                AddLocalMinPoly(e1, e2, pt);
                            break;
                        case ClipType.Xor:
                            AddLocalMinPoly(e1, e2, pt);
                            break;
                    }
                else
                    SwapSides(e1, e2);
            }
        }
        //------------------------------------------------------------------------------

        private void DeleteFromAEL(TEdge e)
        {
            TEdge AelPrev = e.PrevInAEL;
            TEdge AelNext = e.NextInAEL;
            if (AelPrev == null && AelNext == null && (e != m_ActiveEdges))
                return; //already deleted
            if (AelPrev != null)
                AelPrev.NextInAEL = AelNext;
            else m_ActiveEdges = AelNext;
            if (AelNext != null)
                AelNext.PrevInAEL = AelPrev;
            e.NextInAEL = null;
            e.PrevInAEL = null;
        }
        //------------------------------------------------------------------------------

        private void DeleteFromSEL(TEdge e)
        {
            TEdge SelPrev = e.PrevInSEL;
            TEdge SelNext = e.NextInSEL;
            if (SelPrev == null && SelNext == null && (e != m_SortedEdges))
                return; //already deleted
            if (SelPrev != null)
                SelPrev.NextInSEL = SelNext;
            else m_SortedEdges = SelNext;
            if (SelNext != null)
                SelNext.PrevInSEL = SelPrev;
            e.NextInSEL = null;
            e.PrevInSEL = null;
        }
        //------------------------------------------------------------------------------

        private void UpdateEdgeIntoAEL(ref TEdge e)
        {
            if (e.NextInLML == null)
                throw new ClipperException("UpdateEdgeIntoAEL: invalid call");
            TEdge AelPrev = e.PrevInAEL;
            TEdge AelNext = e.NextInAEL;
            e.NextInLML.OutIdx = e.OutIdx;
            if (AelPrev != null)
                AelPrev.NextInAEL = e.NextInLML;
            else m_ActiveEdges = e.NextInLML;
            if (AelNext != null)
                AelNext.PrevInAEL = e.NextInLML;
            e.NextInLML.Side = e.Side;
            e.NextInLML.WindDelta = e.WindDelta;
            e.NextInLML.WindCnt = e.WindCnt;
            e.NextInLML.WindCnt2 = e.WindCnt2;
            e = e.NextInLML;
            e.Curr = e.Bot;
            e.PrevInAEL = AelPrev;
            e.NextInAEL = AelNext;
            if (!IsHorizontal(e)) InsertScanbeam(e.Top.Y);
        }
        //------------------------------------------------------------------------------

        private void ProcessHorizontals(bool isTopOfScanbeam)
        {
            TEdge horzEdge = m_SortedEdges;
            while (horzEdge != null)
            {
                DeleteFromSEL(horzEdge);
                ProcessHorizontal(horzEdge, isTopOfScanbeam);
                horzEdge = m_SortedEdges;
            }
        }
        //------------------------------------------------------------------------------

        void GetHorzDirection(TEdge HorzEdge, out Direction Dir, out long Left, out long Right)
        {
            if (HorzEdge.Bot.X < HorzEdge.Top.X)
            {
                Left = HorzEdge.Bot.X;
                Right = HorzEdge.Top.X;
                Dir = Direction.LeftToRight;
            }
            else
            {
                Left = HorzEdge.Top.X;
                Right = HorzEdge.Bot.X;
                Dir = Direction.RightToLeft;
            }
        }
        //------------------------------------------------------------------------

        private void ProcessHorizontal(TEdge horzEdge, bool isTopOfScanbeam)
        {
            Direction dir;
            long horzLeft, horzRight;

            GetHorzDirection(horzEdge, out dir, out horzLeft, out horzRight);

            TEdge eLastHorz = horzEdge, eMaxPair = null;
            while (eLastHorz.NextInLML != null && IsHorizontal(eLastHorz.NextInLML))
                eLastHorz = eLastHorz.NextInLML;
            if (eLastHorz.NextInLML == null)
                eMaxPair = GetMaximaPair(eLastHorz);

            for (;;)
            {
                bool IsLastHorz = (horzEdge == eLastHorz);
                TEdge e = GetNextInAEL(horzEdge, dir);
                while (e != null)
                {
                    //Break if we've got to the end of an intermediate horizontal edge ...
                    //nb: Smaller Dx's are to the right of larger Dx's ABOVE the horizontal.
                    if (e.Curr.X == horzEdge.Top.X && horzEdge.NextInLML != null &&
                      e.Dx < horzEdge.NextInLML.Dx) break;

                    TEdge eNext = GetNextInAEL(e, dir); //saves eNext for later

                    if ((dir == Direction.LeftToRight && e.Curr.X <= horzRight) ||
                      (dir == Direction.RightToLeft && e.Curr.X >= horzLeft))
                    {
                        //so far we're still in range of the horizontal Edge  but make sure
                        //we're at the last of consec. horizontals when matching with eMaxPair
                        if (e == eMaxPair && IsLastHorz)
                        {
                            if (horzEdge.OutIdx >= 0)
                            {
                                OutPt op1 = AddOutPt(horzEdge, horzEdge.Top);
                                TEdge eNextHorz = m_SortedEdges;
                                while (eNextHorz != null)
                                {
                                    if (eNextHorz.OutIdx >= 0 &&
                                      HorzSegmentsOverlap(horzEdge.Bot.X,
                                      horzEdge.Top.X, eNextHorz.Bot.X, eNextHorz.Top.X))
                                    {
                                        OutPt op2 = AddOutPt(eNextHorz, eNextHorz.Bot);
                                        AddJoin(op2, op1, eNextHorz.Top);
                                    }
                                    eNextHorz = eNextHorz.NextInSEL;
                                }
                                AddGhostJoin(op1, horzEdge.Bot);
                                AddLocalMaxPoly(horzEdge, eMaxPair, horzEdge.Top);
                            }
                            DeleteFromAEL(horzEdge);
                            DeleteFromAEL(eMaxPair);
                            return;
                        }
                        else if (dir == Direction.LeftToRight)
                        {
                            IntPoint Pt = new IntPoint(e.Curr.X, horzEdge.Curr.Y);
                            IntersectEdges(horzEdge, e, Pt);
                        }
                        else
                        {
                            IntPoint Pt = new IntPoint(e.Curr.X, horzEdge.Curr.Y);
                            IntersectEdges(e, horzEdge, Pt);
                        }
                        SwapPositionsInAEL(horzEdge, e);
                    }
                    else if ((dir == Direction.LeftToRight && e.Curr.X >= horzRight) ||
                      (dir == Direction.RightToLeft && e.Curr.X <= horzLeft)) break;
                    e = eNext;
                } //end while

                if (horzEdge.NextInLML != null && IsHorizontal(horzEdge.NextInLML))
                {
                    UpdateEdgeIntoAEL(ref horzEdge);
                    if (horzEdge.OutIdx >= 0) AddOutPt(horzEdge, horzEdge.Bot);
                    GetHorzDirection(horzEdge, out dir, out horzLeft, out horzRight);
                }
                else
                    break;
            } //end for (;;)

            if (horzEdge.NextInLML != null)
            {
                if (horzEdge.OutIdx >= 0)
                {
                    OutPt op1 = AddOutPt(horzEdge, horzEdge.Top);
                    if (isTopOfScanbeam) AddGhostJoin(op1, horzEdge.Bot);

                    UpdateEdgeIntoAEL(ref horzEdge);
                    if (horzEdge.WindDelta == 0) return;
                    //nb: HorzEdge is no longer horizontal here
                    TEdge ePrev = horzEdge.PrevInAEL;
                    TEdge eNext = horzEdge.NextInAEL;
                    if (ePrev != null && ePrev.Curr.X == horzEdge.Bot.X &&
                      ePrev.Curr.Y == horzEdge.Bot.Y && ePrev.WindDelta != 0 &&
                      (ePrev.OutIdx >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                      SlopesEqual(horzEdge, ePrev, MUseFullRange)))
                    {
                        OutPt op2 = AddOutPt(ePrev, horzEdge.Bot);
                        AddJoin(op1, op2, horzEdge.Top);
                    }
                    else if (eNext != null && eNext.Curr.X == horzEdge.Bot.X &&
                      eNext.Curr.Y == horzEdge.Bot.Y && eNext.WindDelta != 0 &&
                      eNext.OutIdx >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                      SlopesEqual(horzEdge, eNext, MUseFullRange))
                    {
                        OutPt op2 = AddOutPt(eNext, horzEdge.Bot);
                        AddJoin(op1, op2, horzEdge.Top);
                    }
                }
                else
                    UpdateEdgeIntoAEL(ref horzEdge);
            }
            else
            {
                if (horzEdge.OutIdx >= 0) AddOutPt(horzEdge, horzEdge.Top);
                DeleteFromAEL(horzEdge);
            }
        }
        //------------------------------------------------------------------------------

        private static TEdge GetNextInAEL(TEdge e, Direction direction)
        {
            return direction == Direction.LeftToRight ? e.NextInAEL : e.PrevInAEL;
        }
        //------------------------------------------------------------------------------

        private bool IsMinima(TEdge e)
        {
            return e != null && (e.Prev.NextInLML != e) && (e.Next.NextInLML != e);
        }
        //------------------------------------------------------------------------------

        private static bool IsMaxima(TEdge e, double y)
        {
            return (e != null && ((double)e.Top.Y).IsPracticallySame(y) && e.NextInLML == null);
        }
        //------------------------------------------------------------------------------

        private static bool IsIntermediate(TEdge e, double y)
        {
            return (((double)e.Top.Y).IsPracticallySame(y) && e.NextInLML != null);
        }
        //------------------------------------------------------------------------------

        private static TEdge GetMaximaPair(TEdge e)
        {
            TEdge result = null;
            if ((e.Next.Top == e.Top) && e.Next.NextInLML == null)
                result = e.Next;
            else if ((e.Prev.Top == e.Top) && e.Prev.NextInLML == null)
                result = e.Prev;
            if (result != null && (result.OutIdx == Skip ||
              (result.NextInAEL == result.PrevInAEL && !IsHorizontal(result))))
                return null;
            return result;
        }
        //------------------------------------------------------------------------------

        private bool ProcessIntersections(long topY)
        {
            if (m_ActiveEdges == null) return true;
            try
            {
                BuildIntersectList(topY);
                if (m_IntersectList.Count == 0) return true;
                if (m_IntersectList.Count == 1 || FixupIntersectionOrder())
                    ProcessIntersectList();
                else
                    return false;
            }
            catch
            {
                m_SortedEdges = null;
                m_IntersectList.Clear();
                throw new ClipperException("ProcessIntersections error");
            }
            m_SortedEdges = null;
            return true;
        }
        //------------------------------------------------------------------------------

        private void BuildIntersectList(long topY)
        {
            if (m_ActiveEdges == null) return;

            //prepare for sorting ...
            TEdge e = m_ActiveEdges;
            m_SortedEdges = e;
            while (e != null)
            {
                e.PrevInSEL = e.PrevInAEL;
                e.NextInSEL = e.NextInAEL;
                e.Curr.X = TopX(e, topY);
                e = e.NextInAEL;
            }

            //bubblesort ...
            bool isModified = true;
            while (isModified && m_SortedEdges != null)
            {
                isModified = false;
                e = m_SortedEdges;
                while (e.NextInSEL != null)
                {
                    TEdge eNext = e.NextInSEL;
                    IntPoint pt;
                    if (e.Curr.X > eNext.Curr.X)
                    {
                        IntersectPoint(e, eNext, out pt);
                        IntersectNode newNode = new IntersectNode();
                        newNode.Edge1 = e;
                        newNode.Edge2 = eNext;
                        newNode.Pt = pt;
                        m_IntersectList.Add(newNode);

                        SwapPositionsInSEL(e, eNext);
                        isModified = true;
                    }
                    else
                        e = eNext;
                }
                if (e.PrevInSEL != null) e.PrevInSEL.NextInSEL = null;
                else break;
            }
            m_SortedEdges = null;
        }
        //------------------------------------------------------------------------------

        private bool EdgesAdjacent(IntersectNode inode)
        {
            return (inode.Edge1.NextInSEL == inode.Edge2) ||
              (inode.Edge1.PrevInSEL == inode.Edge2);
        }
        //------------------------------------------------------------------------------

        private static int IntersectNodeSort(IntersectNode node1, IntersectNode node2)
        {
            //the following typecast is safe because the differences in Pt.Y will
            //be limited to the height of the scanbeam.
            return (int)(node2.Pt.Y - node1.Pt.Y);
        }
        //------------------------------------------------------------------------------

        private bool FixupIntersectionOrder()
        {
            //pre-condition: intersections are sorted bottom-most first.
            //Now it's crucial that intersections are made only between adjacent edges,
            //so to ensure this the order of intersections may need adjusting ...
            m_IntersectList.Sort(m_IntersectNodeComparer);

            CopyAELToSEL();
            int cnt = m_IntersectList.Count;
            for (int i = 0; i < cnt; i++)
            {
                if (!EdgesAdjacent(m_IntersectList[i]))
                {
                    int j = i + 1;
                    while (j < cnt && !EdgesAdjacent(m_IntersectList[j])) j++;
                    if (j == cnt) return false;

                    IntersectNode tmp = m_IntersectList[i];
                    m_IntersectList[i] = m_IntersectList[j];
                    m_IntersectList[j] = tmp;

                }
                SwapPositionsInSEL(m_IntersectList[i].Edge1, m_IntersectList[i].Edge2);
            }
            return true;
        }
        //------------------------------------------------------------------------------

        private void ProcessIntersectList()
        {
            for (int i = 0; i < m_IntersectList.Count; i++)
            {
                IntersectNode iNode = m_IntersectList[i];
                {
                    IntersectEdges(iNode.Edge1, iNode.Edge2, iNode.Pt);
                    SwapPositionsInAEL(iNode.Edge1, iNode.Edge2);
                }
            }
            m_IntersectList.Clear();
        }
        //------------------------------------------------------------------------------

        internal static long Round(double value)
        {
            return value < 0 ? (long)(value - 0.5) : (long)(value + 0.5);
        }
        //------------------------------------------------------------------------------

        private static long TopX(TEdge edge, long currentY)
        {
            if (currentY == edge.Top.Y)
                return edge.Top.X;
            return edge.Bot.X + Round(edge.Dx * (currentY - edge.Bot.Y));
        }
        //------------------------------------------------------------------------------

        private static void IntersectPoint(TEdge edge1, TEdge edge2, out IntPoint ip)
        {
            ip = new IntPoint();
            double b1, b2;
            //nb: with very large coordinate values, it's possible for SlopesEqual() to 
            //return false but for the edge.Dx value be equal due to double precision rounding.
            if (edge1.Dx.IsPracticallySame(edge2.Dx))
            {
                ip.Y = edge1.Curr.Y;
                ip.X = TopX(edge1, ip.Y);
                return;
            }

            if (edge1.Delta.X == 0)
            {
                ip.X = edge1.Bot.X;
                if (IsHorizontal(edge2))
                {
                    ip.Y = edge2.Bot.Y;
                }
                else
                {
                    b2 = edge2.Bot.Y - (edge2.Bot.X / edge2.Dx);
                    ip.Y = Round(ip.X / edge2.Dx + b2);
                }
            }
            else if (edge2.Delta.X == 0)
            {
                ip.X = edge2.Bot.X;
                if (IsHorizontal(edge1))
                {
                    ip.Y = edge1.Bot.Y;
                }
                else
                {
                    b1 = edge1.Bot.Y - (edge1.Bot.X / edge1.Dx);
                    ip.Y = Round(ip.X / edge1.Dx + b1);
                }
            }
            else
            {
                b1 = edge1.Bot.X - edge1.Bot.Y * edge1.Dx;
                b2 = edge2.Bot.X - edge2.Bot.Y * edge2.Dx;
                var q = (b2 - b1) / (edge1.Dx - edge2.Dx);
                ip.Y = Round(q);
                ip.X = Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx) ? Round(edge1.Dx * q + b1) : Round(edge2.Dx * q + b2);
            }

            if (ip.Y < edge1.Top.Y || ip.Y < edge2.Top.Y)
            {
                ip.Y = edge1.Top.Y > edge2.Top.Y ? edge1.Top.Y : edge2.Top.Y;
                ip.X = TopX(Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx) ? edge1 : edge2, ip.Y);
            }
            //finally, don't allow 'ip' to be BELOW curr.Y (ie bottom of scanbeam) ...
            if (ip.Y <= edge1.Curr.Y) return;
            ip.Y = edge1.Curr.Y;
            //better to use the more vertical edge to derive X ...
            ip.X = TopX(Math.Abs(edge1.Dx) > Math.Abs(edge2.Dx) ? edge2 : edge1, ip.Y);
        }
        //------------------------------------------------------------------------------

        private void ProcessEdgesAtTopOfScanbeam(long topY)
        {
            TEdge e = m_ActiveEdges;
            while (e != null)
            {
                //1. process maxima, treating them as if they're 'bent' horizontal edges,
                //   but exclude maxima with horizontal edges. nb: e can't be a horizontal.
                bool IsMaximaEdge = IsMaxima(e, topY);

                if (IsMaximaEdge)
                {
                    TEdge eMaxPair = GetMaximaPair(e);
                    IsMaximaEdge = (eMaxPair == null || !IsHorizontal(eMaxPair));
                }

                if (IsMaximaEdge)
                {
                    TEdge ePrev = e.PrevInAEL;
                    DoMaxima(e);
                    if (ePrev == null) e = m_ActiveEdges;
                    else e = ePrev.NextInAEL;
                }
                else
                {
                    //2. promote horizontal edges, otherwise update Curr.X and Curr.Y ...
                    if (IsIntermediate(e, topY) && IsHorizontal(e.NextInLML))
                    {
                        UpdateEdgeIntoAEL(ref e);
                        if (e.OutIdx >= 0)
                            AddOutPt(e, e.Bot);
                        AddEdgeToSEL(e);
                    }
                    else
                    {
                        e.Curr.X = TopX(e, topY);
                        e.Curr.Y = topY;
                    }

                    if (StrictlySimple)
                    {
                        TEdge ePrev = e.PrevInAEL;
                        if ((e.OutIdx >= 0) && (e.WindDelta != 0) && ePrev != null &&
                          (ePrev.OutIdx >= 0) && (ePrev.Curr.X == e.Curr.X) &&
                          (ePrev.WindDelta != 0))
                        {
                            IntPoint ip = new IntPoint(e.Curr);
#if use_xyz
                SetZ(ref ip, ePrev, e);
#endif
                            OutPt op = AddOutPt(ePrev, ip);
                            OutPt op2 = AddOutPt(e, ip);
                            AddJoin(op, op2, ip); //StrictlySimple (type-3) join
                        }
                    }

                    e = e.NextInAEL;
                }
            }

            //3. Process horizontals at the Top of the scanbeam ...
            ProcessHorizontals(true);

            //4. Promote intermediate vertices ...
            e = m_ActiveEdges;
            while (e != null)
            {
                if (IsIntermediate(e, topY))
                {
                    OutPt op = null;
                    if (e.OutIdx >= 0)
                        op = AddOutPt(e, e.Top);
                    UpdateEdgeIntoAEL(ref e);

                    //if output polygons share an edge, they'll need joining later ...
                    TEdge ePrev = e.PrevInAEL;
                    TEdge eNext = e.NextInAEL;
                    if (ePrev != null && ePrev.Curr.X == e.Bot.X &&
                      ePrev.Curr.Y == e.Bot.Y && op != null &&
                      ePrev.OutIdx >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                      SlopesEqual(e, ePrev, MUseFullRange) &&
                      (e.WindDelta != 0) && (ePrev.WindDelta != 0))
                    {
                        OutPt op2 = AddOutPt(ePrev, e.Bot);
                        AddJoin(op, op2, e.Top);
                    }
                    else if (eNext != null && eNext.Curr.X == e.Bot.X &&
                      eNext.Curr.Y == e.Bot.Y && op != null &&
                      eNext.OutIdx >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                      SlopesEqual(e, eNext, MUseFullRange) &&
                      (e.WindDelta != 0) && (eNext.WindDelta != 0))
                    {
                        OutPt op2 = AddOutPt(eNext, e.Bot);
                        AddJoin(op, op2, e.Top);
                    }
                }
                e = e.NextInAEL;
            }
        }
        //------------------------------------------------------------------------------

        private void DoMaxima(TEdge e)
        {
            TEdge eMaxPair = GetMaximaPair(e);
            if (eMaxPair == null)
            {
                if (e.OutIdx >= 0)
                    AddOutPt(e, e.Top);
                DeleteFromAEL(e);
                return;
            }

            TEdge eNext = e.NextInAEL;
            while (eNext != null && eNext != eMaxPair)
            {
                IntersectEdges(e, eNext, e.Top);
                SwapPositionsInAEL(e, eNext);
                eNext = e.NextInAEL;
            }

            if (e.OutIdx == Unassigned && eMaxPair.OutIdx == Unassigned)
            {
                DeleteFromAEL(e);
                DeleteFromAEL(eMaxPair);
            }
            else if (e.OutIdx >= 0 && eMaxPair.OutIdx >= 0)
            {
                if (e.OutIdx >= 0) AddLocalMaxPoly(e, eMaxPair, e.Top);
                DeleteFromAEL(e);
                DeleteFromAEL(eMaxPair);
            }
            else if (e.WindDelta == 0)
            {
                if (e.OutIdx >= 0)
                {
                    AddOutPt(e, e.Top);
                    e.OutIdx = Unassigned;
                }
                DeleteFromAEL(e);

                if (eMaxPair.OutIdx >= 0)
                {
                    AddOutPt(eMaxPair, e.Top);
                    eMaxPair.OutIdx = Unassigned;
                }
                DeleteFromAEL(eMaxPair);
            }
            else throw new ClipperException("DoMaxima error");
        }
        //------------------------------------------------------------------------------

        internal static void ReversePaths(Paths polys)
        {
            foreach (var poly in polys) { poly.Reverse(); }
        }
        //------------------------------------------------------------------------------

        internal static bool Orientation(Path poly)
        {
            return Area(poly) >= 0;
        }
        //------------------------------------------------------------------------------

        private int PointCount(OutPt pts)
        {
            if (pts == null) return 0;
            int result = 0;
            OutPt p = pts;
            do
            {
                result++;
                p = p.Next;
            }
            while (p != pts);
            return result;
        }
        //------------------------------------------------------------------------------

        private void BuildResult(Paths polyg)
        {
            polyg.Clear();
            polyg.Capacity = m_PolyOuts.Count;
            foreach (var outRec in m_PolyOuts)
            {
                if (outRec.Pts == null) continue;
                OutPt p = outRec.Pts.Prev;
                int cnt = PointCount(p);
                if (cnt < 2) continue;
                Path pg = new Path(cnt);
                for (var j = 0; j < cnt; j++)
                {
                    pg.Add(p.Pt);
                    p = p.Prev;
                }
                polyg.Add(pg);
            }
        }
        //------------------------------------------------------------------------------

        private void BuildResult2(PolyTree polytree)
        {
            polytree.Clear();

            //add each output polygon/contour to polytree ...
            polytree.MAllPolys.Capacity = m_PolyOuts.Count;
            foreach (var outRec in m_PolyOuts)
            {
                var cnt = PointCount(outRec.Pts);
                if ((outRec.IsOpen && cnt < 2) ||
                    (!outRec.IsOpen && cnt < 3)) continue;
                FixHoleLinkage(outRec);
                var pn = new PolyNode();
                polytree.MAllPolys.Add(pn);
                outRec.PolyNode = pn;
                pn.MPolygon.Capacity = cnt;
                var op = outRec.Pts.Prev;
                for (var j = 0; j < cnt; j++)
                {
                    pn.MPolygon.Add(op.Pt);
                    op = op.Prev;
                }
            }

            //fixup PolyNode links etc ...
            polytree.MChilds.Capacity = m_PolyOuts.Count;
            foreach (var outRec in m_PolyOuts.Where(outRec => outRec.PolyNode != null))
            {
                if (outRec.IsOpen)
                {
                    outRec.PolyNode.IsOpen = true;
                    polytree.AddChild(outRec.PolyNode);
                }
                else if (outRec.FirstLeft?.PolyNode != null)
                    outRec.FirstLeft.PolyNode.AddChild(outRec.PolyNode);
                else
                    polytree.AddChild(outRec.PolyNode);
            }
        }
        //------------------------------------------------------------------------------

        private void FixupOutPolygon(OutRec outRec)
        {
            //FixupOutPolygon() - removes duplicate points and simplifies consecutive
            //parallel edges by removing the middle vertex.
            OutPt lastOK = null;
            outRec.BottomPt = null;
            OutPt pp = outRec.Pts;
            for (;;)
            {
                if (pp.Prev == pp || pp.Prev == pp.Next)
                {
                    outRec.Pts = null;
                    return;
                }
                //test for duplicate points and collinear edges ...
                if ((pp.Pt == pp.Next.Pt) || (pp.Pt == pp.Prev.Pt) ||
                  (SlopesEqual(pp.Prev.Pt, pp.Pt, pp.Next.Pt, MUseFullRange) &&
                  (!PreserveCollinear || !Pt2IsBetweenPt1AndPt3(pp.Prev.Pt, pp.Pt, pp.Next.Pt))))
                {
                    lastOK = null;
                    pp.Prev.Next = pp.Next;
                    pp.Next.Prev = pp.Prev;
                    pp = pp.Prev;
                }
                else if (pp == lastOK) break;
                else
                {
                    if (lastOK == null) lastOK = pp;
                    pp = pp.Next;
                }
            }
            outRec.Pts = pp;
        }
        //------------------------------------------------------------------------------

        OutPt DupOutPt(OutPt outPt, bool InsertAfter)
        {
            OutPt result = new OutPt();
            result.Pt = outPt.Pt;
            result.Idx = outPt.Idx;
            if (InsertAfter)
            {
                result.Next = outPt.Next;
                result.Prev = outPt;
                outPt.Next.Prev = result;
                outPt.Next = result;
            }
            else
            {
                result.Prev = outPt.Prev;
                result.Next = outPt;
                outPt.Prev.Next = result;
                outPt.Prev = result;
            }
            return result;
        }
        //------------------------------------------------------------------------------

        bool GetOverlap(long a1, long a2, long b1, long b2, out long Left, out long Right)
        {
            if (a1 < a2)
            {
                if (b1 < b2) { Left = Math.Max(a1, b1); Right = Math.Min(a2, b2); }
                else { Left = Math.Max(a1, b2); Right = Math.Min(a2, b1); }
            }
            else
            {
                if (b1 < b2) { Left = Math.Max(a2, b1); Right = Math.Min(a1, b2); }
                else { Left = Math.Max(a2, b2); Right = Math.Min(a1, b1); }
            }
            return Left < Right;
        }
        //------------------------------------------------------------------------------

        bool JoinHorz(OutPt op1, OutPt op1b, OutPt op2, OutPt op2b,
          IntPoint Pt, bool DiscardLeft)
        {
            Direction Dir1 = (op1.Pt.X > op1b.Pt.X ?
              Direction.RightToLeft : Direction.LeftToRight);
            Direction Dir2 = (op2.Pt.X > op2b.Pt.X ?
              Direction.RightToLeft : Direction.LeftToRight);
            if (Dir1 == Dir2) return false;

            //When DiscardLeft, we want Op1b to be on the Left of outPoint1, otherwise we
            //want Op1b to be on the Right. (And likewise with outPoint2 and Op2b.)
            //So, to facilitate this while inserting Op1b and Op2b ...
            //when DiscardLeft, make sure we're AT or RIGHT of Pt before adding Op1b,
            //otherwise make sure we're AT or LEFT of Pt. (Likewise with Op2b.)
            if (Dir1 == Direction.LeftToRight)
            {
                while (op1.Next.Pt.X <= Pt.X &&
                  op1.Next.Pt.X >= op1.Pt.X && op1.Next.Pt.Y == Pt.Y)
                    op1 = op1.Next;
                if (DiscardLeft && (op1.Pt.X != Pt.X)) op1 = op1.Next;
                op1b = DupOutPt(op1, !DiscardLeft);
                if (op1b.Pt != Pt)
                {
                    op1 = op1b;
                    op1.Pt = Pt;
                    op1b = DupOutPt(op1, !DiscardLeft);
                }
            }
            else
            {
                while (op1.Next.Pt.X >= Pt.X &&
                  op1.Next.Pt.X <= op1.Pt.X && op1.Next.Pt.Y == Pt.Y)
                    op1 = op1.Next;
                if (!DiscardLeft && (op1.Pt.X != Pt.X)) op1 = op1.Next;
                op1b = DupOutPt(op1, DiscardLeft);
                if (op1b.Pt != Pt)
                {
                    op1 = op1b;
                    op1.Pt = Pt;
                    op1b = DupOutPt(op1, DiscardLeft);
                }
            }

            if (Dir2 == Direction.LeftToRight)
            {
                while (op2.Next.Pt.X <= Pt.X &&
                  op2.Next.Pt.X >= op2.Pt.X && op2.Next.Pt.Y == Pt.Y)
                    op2 = op2.Next;
                if (DiscardLeft && (op2.Pt.X != Pt.X)) op2 = op2.Next;
                op2b = DupOutPt(op2, !DiscardLeft);
                if (op2b.Pt != Pt)
                {
                    op2 = op2b;
                    op2.Pt = Pt;
                    op2b = DupOutPt(op2, !DiscardLeft);
                }
            }
            else
            {
                while (op2.Next.Pt.X >= Pt.X &&
                  op2.Next.Pt.X <= op2.Pt.X && op2.Next.Pt.Y == Pt.Y)
                    op2 = op2.Next;
                if (!DiscardLeft && (op2.Pt.X != Pt.X)) op2 = op2.Next;
                op2b = DupOutPt(op2, DiscardLeft);
                if (op2b.Pt != Pt)
                {
                    op2 = op2b;
                    op2.Pt = Pt;
                    op2b = DupOutPt(op2, DiscardLeft);
                }
            }

            if ((Dir1 == Direction.LeftToRight) == DiscardLeft)
            {
                op1.Prev = op2;
                op2.Next = op1;
                op1b.Next = op2b;
                op2b.Prev = op1b;
            }
            else
            {
                op1.Next = op2;
                op2.Prev = op1;
                op1b.Prev = op2b;
                op2b.Next = op1b;
            }
            return true;
        }
        //------------------------------------------------------------------------------

        private bool JoinPoints(Join j, OutRec outRec1, OutRec outRec2)
        {
            OutPt op1 = j.OutPt1, op1b;
            OutPt op2 = j.OutPt2, op2b;

            //There are 3 kinds of joins for output polygons ...
            //1. Horizontal joins where Join.OutPt1 & Join.OutPt2 are a vertices anywhere
            //along (horizontal) collinear edges (& Join.OffPt is on the same horizontal).
            //2. Non-horizontal joins where Join.OutPt1 & Join.OutPt2 are at the same
            //location at the Bottom of the overlapping segment (& Join.OffPt is above).
            //3. StrictlySimple joins where edges touch but are not collinear and where
            //Join.OutPt1, Join.OutPt2 & Join.OffPt all share the same point.
            bool isHorizontal = (j.OutPt1.Pt.Y == j.OffPt.Y);

            if (isHorizontal && (j.OffPt == j.OutPt1.Pt) && (j.OffPt == j.OutPt2.Pt))
            {
                //Strictly Simple join ...
                if (outRec1 != outRec2) return false;
                op1b = j.OutPt1.Next;
                while (op1b != op1 && (op1b.Pt == j.OffPt))
                    op1b = op1b.Next;
                bool reverse1 = (op1b.Pt.Y > j.OffPt.Y);
                op2b = j.OutPt2.Next;
                while (op2b != op2 && (op2b.Pt == j.OffPt))
                    op2b = op2b.Next;
                bool reverse2 = (op2b.Pt.Y > j.OffPt.Y);
                if (reverse1 == reverse2) return false;
                if (reverse1)
                {
                    op1b = DupOutPt(op1, false);
                    op2b = DupOutPt(op2, true);
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                else
                {
                    op1b = DupOutPt(op1, true);
                    op2b = DupOutPt(op2, false);
                    op1.Next = op2;
                    op2.Prev = op1;
                    op1b.Prev = op2b;
                    op2b.Next = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
            }
            else if (isHorizontal)
            {
                //treat horizontal joins differently to non-horizontal joins since with
                //them we're not yet sure where the overlapping is. OutPt1.Pt & OutPt2.Pt
                //may be anywhere along the horizontal edge.
                op1b = op1;
                while (op1.Prev.Pt.Y == op1.Pt.Y && op1.Prev != op1b && op1.Prev != op2)
                    op1 = op1.Prev;
                while (op1b.Next.Pt.Y == op1b.Pt.Y && op1b.Next != op1 && op1b.Next != op2)
                    op1b = op1b.Next;
                if (op1b.Next == op1 || op1b.Next == op2) return false; //a flat 'polygon'

                op2b = op2;
                while (op2.Prev.Pt.Y == op2.Pt.Y && op2.Prev != op2b && op2.Prev != op1b)
                    op2 = op2.Prev;
                while (op2b.Next.Pt.Y == op2b.Pt.Y && op2b.Next != op2 && op2b.Next != op1)
                    op2b = op2b.Next;
                if (op2b.Next == op2 || op2b.Next == op1) return false; //a flat 'polygon'

                long Left, Right;
                //outPoint1 -. Op1b & outPoint2 -. Op2b are the extremites of the horizontal edges
                if (!GetOverlap(op1.Pt.X, op1b.Pt.X, op2.Pt.X, op2b.Pt.X, out Left, out Right))
                    return false;

                //DiscardLeftSide: when overlapping edges are joined, a spike will created
                //which needs to be cleaned up. However, we don't want outPoint1 or outPoint2 caught up
                //on the discard Side as either may still be needed for other joins ...
                IntPoint Pt;
                bool DiscardLeftSide;
                if (op1.Pt.X >= Left && op1.Pt.X <= Right)
                {
                    Pt = op1.Pt; DiscardLeftSide = (op1.Pt.X > op1b.Pt.X);
                }
                else if (op2.Pt.X >= Left && op2.Pt.X <= Right)
                {
                    Pt = op2.Pt; DiscardLeftSide = (op2.Pt.X > op2b.Pt.X);
                }
                else if (op1b.Pt.X >= Left && op1b.Pt.X <= Right)
                {
                    Pt = op1b.Pt; DiscardLeftSide = op1b.Pt.X > op1.Pt.X;
                }
                else
                {
                    Pt = op2b.Pt; DiscardLeftSide = (op2b.Pt.X > op2.Pt.X);
                }
                j.OutPt1 = op1;
                j.OutPt2 = op2;
                return JoinHorz(op1, op1b, op2, op2b, Pt, DiscardLeftSide);
            }
            else
            {
                //nb: For non-horizontal joins ...
                //    1. Jr.OutPt1.Pt.Y == Jr.OutPt2.Pt.Y
                //    2. Jr.OutPt1.Pt > Jr.OffPt.Y

                //make sure the polygons are correctly oriented ...
                op1b = op1.Next;
                while ((op1b.Pt == op1.Pt) && (op1b != op1)) op1b = op1b.Next;
                var reverse1 = ((op1b.Pt.Y > op1.Pt.Y) ||
                  !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt, MUseFullRange));
                if (reverse1)
                {
                    op1b = op1.Prev;
                    while ((op1b.Pt == op1.Pt) && (op1b != op1)) op1b = op1b.Prev;
                    if ((op1b.Pt.Y > op1.Pt.Y) ||
                      !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt, MUseFullRange)) return false;
                }
                op2b = op2.Next;
                while ((op2b.Pt == op2.Pt) && (op2b != op2)) op2b = op2b.Next;
                var reverse2 = ((op2b.Pt.Y > op2.Pt.Y) ||
                  !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt, MUseFullRange));
                if (reverse2)
                {
                    op2b = op2.Prev;
                    while ((op2b.Pt == op2.Pt) && (op2b != op2)) op2b = op2b.Prev;
                    if ((op2b.Pt.Y > op2.Pt.Y) ||
                      !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt, MUseFullRange)) return false;
                }

                if ((op1b == op1) || (op2b == op2) || (op1b == op2b) ||
                  ((outRec1 == outRec2) && (reverse1 == reverse2))) return false;

                if (reverse1)
                {
                    op1b = DupOutPt(op1, false);
                    op2b = DupOutPt(op2, true);
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                else
                {
                    op1b = DupOutPt(op1, true);
                    op2b = DupOutPt(op2, false);
                    op1.Next = op2;
                    op2.Prev = op1;
                    op1b.Prev = op2b;
                    op2b.Next = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
            }
        }
        //----------------------------------------------------------------------

        internal static int PointInPolygon(IntPoint pt, Path path)
        {
            //returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            //See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
            //http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
            int result = 0, cnt = path.Count;
            if (cnt < 3) return 0;
            var ip = path[0];
            for (var i = 1; i <= cnt; ++i)
            {
                var ipNext = (i == cnt ? path[0] : path[i]);
                if (ipNext.Y == pt.Y)
                {
                    if ((ipNext.X == pt.X) || (ip.Y == pt.Y &&
                      ((ipNext.X > pt.X) == (ip.X < pt.X)))) return -1;
                }
                if ((ip.Y < pt.Y) != (ipNext.Y < pt.Y))
                {
                    if (ip.X >= pt.X)
                    {
                        if (ipNext.X > pt.X) result = 1 - result;
                        else
                        {
                            var d = (double)(ip.X - pt.X) * (ipNext.Y - pt.Y) -
                              (double)(ipNext.X - pt.X) * (ip.Y - pt.Y);
                            if (d.IsNegligible()) return -1;
                            else if ((d > 0) == (ipNext.Y > ip.Y)) result = 1 - result;
                        }
                    }
                    else
                    {
                        if (ipNext.X > pt.X)
                        {
                            var d = (double)(ip.X - pt.X) * (ipNext.Y - pt.Y) -
                              (double)(ipNext.X - pt.X) * (ip.Y - pt.Y);
                            if (d.IsNegligible()) return -1;
                            else if ((d > 0) == (ipNext.Y > ip.Y)) result = 1 - result;
                        }
                    }
                }
                ip = ipNext;
            }
            return result;
        }
        //------------------------------------------------------------------------------

        private static int PointInPolygon(IntPoint pt, OutPt op)
        {
            //returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            //See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
            //http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
            var result = 0;
            var startOp = op;
            long ptx = pt.X, pty = pt.Y;
            long poly0X = op.Pt.X, poly0Y = op.Pt.Y;
            do
            {
                op = op.Next;
                long poly1X = op.Pt.X, poly1Y = op.Pt.Y;

                if (poly1Y == pty)
                {
                    if ((poly1X == ptx) || (poly0Y == pty &&
                      ((poly1X > ptx) == (poly0X < ptx)))) return -1;
                }
                if ((poly0Y < pty) != (poly1Y < pty))
                {
                    if (poly0X >= ptx)
                    {
                        if (poly1X > ptx) result = 1 - result;
                        else
                        {
                            var d = (double)(poly0X - ptx) * (poly1Y - pty) -
                              (double)(poly1X - ptx) * (poly0Y - pty);
                            if (d.IsNegligible()) return -1;
                            if ((d > 0) == (poly1Y > poly0Y)) result = 1 - result;
                        }
                    }
                    else
                    {
                        if (poly1X > ptx)
                        {
                            var d = (double)(poly0X - ptx) * (poly1Y - pty) -
                              (double)(poly1X - ptx) * (poly0Y - pty);
                            if (d.IsNegligible()) return -1;
                            if ((d > 0) == (poly1Y > poly0Y)) result = 1 - result;
                        }
                    }
                }
                poly0X = poly1X; poly0Y = poly1Y;
            } while (startOp != op);
            return result;
        }
        //------------------------------------------------------------------------------

        private static bool Poly2ContainsPoly1(OutPt outPt1, OutPt outPt2)
        {
            OutPt op = outPt1;
            do
            {
                //nb: PointInPolygon returns 0 if false, +1 if true, -1 if pt on polygon
                int res = PointInPolygon(op.Pt, outPt2);
                if (res >= 0) return res > 0;
                op = op.Next;
            }
            while (op != outPt1);
            return true;
        }
        //----------------------------------------------------------------------

        private void FixupFirstLefts1(OutRec OldOutRec, OutRec NewOutRec)
        {
            for (int i = 0; i < m_PolyOuts.Count; i++)
            {
                OutRec outRec = m_PolyOuts[i];
                if (outRec.Pts == null || outRec.FirstLeft == null) continue;
                OutRec firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (firstLeft == OldOutRec)
                {
                    if (Poly2ContainsPoly1(outRec.Pts, NewOutRec.Pts))
                        outRec.FirstLeft = NewOutRec;
                }
            }
        }
        //----------------------------------------------------------------------

        private void FixupFirstLefts2(OutRec OldOutRec, OutRec NewOutRec)
        {
            foreach (OutRec outRec in m_PolyOuts)
                if (outRec.FirstLeft == OldOutRec) outRec.FirstLeft = NewOutRec;
        }
        //----------------------------------------------------------------------

        private static OutRec ParseFirstLeft(OutRec FirstLeft)
        {
            while (FirstLeft != null && FirstLeft.Pts == null)
                FirstLeft = FirstLeft.FirstLeft;
            return FirstLeft;
        }
        //------------------------------------------------------------------------------

        private void JoinCommonEdges()
        {
            for (int i = 0; i < m_Joins.Count; i++)
            {
                Join join = m_Joins[i];

                OutRec outRec1 = GetOutRec(join.OutPt1.Idx);
                OutRec outRec2 = GetOutRec(join.OutPt2.Idx);

                if (outRec1.Pts == null || outRec2.Pts == null) continue;

                //get the polygon fragment with the correct hole state (FirstLeft)
                //before calling JoinPoints() ...
                OutRec holeStateRec;
                if (outRec1 == outRec2) holeStateRec = outRec1;
                else if (Param1RightOfParam2(outRec1, outRec2)) holeStateRec = outRec2;
                else if (Param1RightOfParam2(outRec2, outRec1)) holeStateRec = outRec1;
                else holeStateRec = GetLowermostRec(outRec1, outRec2);

                if (!JoinPoints(join, outRec1, outRec2)) continue;

                if (outRec1 == outRec2)
                {
                    //instead of joining two polygons, we've just created a new one by
                    //splitting one polygon into two.
                    outRec1.Pts = join.OutPt1;
                    outRec1.BottomPt = null;
                    outRec2 = CreateOutRec();
                    outRec2.Pts = join.OutPt2;

                    //update all OutRec2.Pts Idx's ...
                    UpdateOutPtIdxs(outRec2);

                    //We now need to check every OutRec.FirstLeft pointer. If it points
                    //to OutRec1 it may need to point to OutRec2 instead ...
                    if (m_UsingPolyTree)
                        for (int j = 0; j < m_PolyOuts.Count - 1; j++)
                        {
                            OutRec oRec = m_PolyOuts[j];
                            if (oRec.Pts == null || ParseFirstLeft(oRec.FirstLeft) != outRec1 ||
                              oRec.IsHole == outRec1.IsHole) continue;
                            if (Poly2ContainsPoly1(oRec.Pts, join.OutPt2))
                                oRec.FirstLeft = outRec2;
                        }

                    if (Poly2ContainsPoly1(outRec2.Pts, outRec1.Pts))
                    {
                        //outRec2 is contained by outRec1 ...
                        outRec2.IsHole = !outRec1.IsHole;
                        outRec2.FirstLeft = outRec1;

                        //fixup FirstLeft pointers that may need reassigning to OutRec1
                        if (m_UsingPolyTree) FixupFirstLefts2(outRec2, outRec1);

                        if ((outRec2.IsHole ^ ReverseSolution) == (Area(outRec2) > 0))
                            ReversePolyPtLinks(outRec2.Pts);

                    }
                    else if (Poly2ContainsPoly1(outRec1.Pts, outRec2.Pts))
                    {
                        //outRec1 is contained by outRec2 ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec1.IsHole = !outRec2.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;
                        outRec1.FirstLeft = outRec2;

                        //fixup FirstLeft pointers that may need reassigning to OutRec1
                        if (m_UsingPolyTree) FixupFirstLefts2(outRec1, outRec2);

                        if ((outRec1.IsHole ^ ReverseSolution) == (Area(outRec1) > 0))
                            ReversePolyPtLinks(outRec1.Pts);
                    }
                    else
                    {
                        //the 2 polygons are completely separate ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;

                        //fixup FirstLeft pointers that may need reassigning to OutRec2
                        if (m_UsingPolyTree) FixupFirstLefts1(outRec1, outRec2);
                    }

                }
                else
                {
                    //joined 2 polygons together ...

                    outRec2.Pts = null;
                    outRec2.BottomPt = null;
                    outRec2.Idx = outRec1.Idx;

                    outRec1.IsHole = holeStateRec.IsHole;
                    if (holeStateRec == outRec2)
                        outRec1.FirstLeft = outRec2.FirstLeft;
                    outRec2.FirstLeft = outRec1;

                    //fixup FirstLeft pointers that may need reassigning to OutRec1
                    if (m_UsingPolyTree) FixupFirstLefts2(outRec2, outRec1);
                }
            }
        }
        //------------------------------------------------------------------------------

        private void UpdateOutPtIdxs(OutRec outrec)
        {
            OutPt op = outrec.Pts;
            do
            {
                op.Idx = outrec.Idx;
                op = op.Prev;
            }
            while (op != outrec.Pts);
        }
        //------------------------------------------------------------------------------

        private void DoSimplePolygons()
        {
            int i = 0;
            while (i < m_PolyOuts.Count)
            {
                OutRec outrec = m_PolyOuts[i++];
                OutPt op = outrec.Pts;
                if (op == null || outrec.IsOpen) continue;
                do //for each Pt in Polygon until duplicate found do ...
                {
                    OutPt op2 = op.Next;
                    while (op2 != outrec.Pts)
                    {
                        if ((op.Pt == op2.Pt) && op2.Next != op && op2.Prev != op)
                        {
                            //split the polygon into two ...
                            OutPt op3 = op.Prev;
                            OutPt op4 = op2.Prev;
                            op.Prev = op4;
                            op4.Next = op;
                            op2.Prev = op3;
                            op3.Next = op2;

                            outrec.Pts = op;
                            OutRec outrec2 = CreateOutRec();
                            outrec2.Pts = op2;
                            UpdateOutPtIdxs(outrec2);
                            if (Poly2ContainsPoly1(outrec2.Pts, outrec.Pts))
                            {
                                //OutRec2 is contained by OutRec1 ...
                                outrec2.IsHole = !outrec.IsHole;
                                outrec2.FirstLeft = outrec;
                                if (m_UsingPolyTree) FixupFirstLefts2(outrec2, outrec);
                            }
                            else
                              if (Poly2ContainsPoly1(outrec.Pts, outrec2.Pts))
                            {
                                //OutRec1 is contained by OutRec2 ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec.IsHole = !outrec2.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                outrec.FirstLeft = outrec2;
                                if (m_UsingPolyTree) FixupFirstLefts2(outrec, outrec2);
                            }
                            else
                            {
                                //the 2 polygons are separate ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                if (m_UsingPolyTree) FixupFirstLefts1(outrec, outrec2);
                            }
                            op2 = op; //ie get ready for the next iteration
                        }
                        op2 = op2.Next;
                    }
                    op = op.Next;
                }
                while (op != outrec.Pts);
            }
        }
        //------------------------------------------------------------------------------

        internal static double Area(Path poly)
        {
            int cnt = poly.Count;
            if (cnt < 3) return 0;
            double a = 0;
            for (int i = 0, j = cnt - 1; i < cnt; ++i)
            {
                a += ((double)poly[j].X + poly[i].X) * ((double)poly[j].Y - poly[i].Y);
                j = i;
            }
            return -a * 0.5;
        }
        //------------------------------------------------------------------------------

        double Area(OutRec outRec)
        {
            OutPt op = outRec.Pts;
            if (op == null) return 0;
            double a = 0;
            do
            {
                a = a + (op.Prev.Pt.X + op.Pt.X) * (op.Prev.Pt.Y - op.Pt.Y);
                op = op.Next;
            } while (op != outRec.Pts);
            return a * 0.5;
        }

        //------------------------------------------------------------------------------
        // SimplifyPolygon functions ...
        // Convert self-intersecting polygons into simple polygons
        //------------------------------------------------------------------------------

        internal static Paths SimplifyPolygon(Path poly,
              PolyFillType fillType = PolyFillType.EvenOdd)
        {
            var result = new Paths();
            var clipper = new Clipper {StrictlySimple = true};
            clipper.AddPath(poly, PolyType.Subject, true);
            clipper.Execute(ClipType.Union, result, fillType, fillType);
            return result;
        }
        //------------------------------------------------------------------------------

        internal static Paths SimplifyPolygons(Paths polys,
            PolyFillType fillType = PolyFillType.EvenOdd)
        {
            var result = new Paths();
            var clipper = new Clipper {StrictlySimple = true};
            clipper.AddPaths(polys, PolyType.Subject, true);
            clipper.Execute(ClipType.Union, result, fillType, fillType);
            return result;
        }
        //------------------------------------------------------------------------------

        private static double DistanceFromLineSqrd(IntPoint pt, IntPoint ln1, IntPoint ln2)
        {
            //The equation of a line in general form (Ax + By + C = 0)
            //given 2 points (x¹,y¹) & (x²,y²) is ...
            //(y¹ - y²)x + (x² - x¹)y + (y² - y¹)x¹ - (x² - x¹)y¹ = 0
            //A = (y¹ - y²); B = (x² - x¹); C = (y² - y¹)x¹ - (x² - x¹)y¹
            //perpendicular distance of point (x³,y³) = (Ax³ + By³ + C)/Sqrt(A² + B²)
            //see http://en.wikipedia.org/wiki/Perpendicular_distance
            double a = ln1.Y - ln2.Y;
            double b = ln2.X - ln1.X;
            var c = a * ln1.X + b * ln1.Y;
            c = a * pt.X + b * pt.Y - c;
            return (c * c) / (a * a + b * b);
        }
        //---------------------------------------------------------------------------

        private static bool SlopesNearCollinear(IntPoint pt1,
            IntPoint pt2, IntPoint pt3, double distSqrd)
        {
            //this function is more accurate when the point that's GEOMETRICALLY 
            //between the other 2 points is the one that's tested for distance.  
            //nb: with 'spikes', either pt1 or pt3 is geometrically between the other pts                    
            if (Math.Abs(pt1.X - pt2.X) > Math.Abs(pt1.Y - pt2.Y))
            {
                if ((pt1.X > pt2.X) == (pt1.X < pt3.X))
                    return DistanceFromLineSqrd(pt1, pt2, pt3) < distSqrd;
                else if ((pt2.X > pt1.X) == (pt2.X < pt3.X))
                    return DistanceFromLineSqrd(pt2, pt1, pt3) < distSqrd;
                else
                    return DistanceFromLineSqrd(pt3, pt1, pt2) < distSqrd;
            }
            else
            {
                if ((pt1.Y > pt2.Y) == (pt1.Y < pt3.Y))
                    return DistanceFromLineSqrd(pt1, pt2, pt3) < distSqrd;
                else if ((pt2.Y > pt1.Y) == (pt2.Y < pt3.Y))
                    return DistanceFromLineSqrd(pt2, pt1, pt3) < distSqrd;
                else
                    return DistanceFromLineSqrd(pt3, pt1, pt2) < distSqrd;
            }
        }
        //------------------------------------------------------------------------------

        private static bool PointsAreClose(IntPoint pt1, IntPoint pt2, double distSqrd)
        {
            var dx = (double)pt1.X - pt2.X;
            var dy = (double)pt1.Y - pt2.Y;
            return ((dx * dx) + (dy * dy) <= distSqrd);
        }
        //------------------------------------------------------------------------------

        private static OutPt ExcludeOp(OutPt op)
        {
            OutPt result = op.Prev;
            result.Next = op.Next;
            op.Next.Prev = result;
            result.Idx = 0;
            return result;
        }
        //------------------------------------------------------------------------------

        internal static Path CleanPolygon(Path path, double distance = 1.415)
        {
            //distance = proximity in units/pixels below which vertices will be stripped. 
            //Default ~= sqrt(2) so when adjacent vertices or semi-adjacent vertices have 
            //both x & y coords within 1 unit, then the second vertex will be stripped.

            var cnt = path.Count;

            if (cnt == 0) return new Path();

            var outPts = new OutPt[cnt];
            for (var i = 0; i < cnt; ++i) outPts[i] = new OutPt();

            for (var i = 0; i < cnt; ++i)
            {
                outPts[i].Pt = path[i];
                outPts[i].Next = outPts[(i + 1) % cnt];
                outPts[i].Next.Prev = outPts[i];
                outPts[i].Idx = 0;
            }

            var distSqrd = distance * distance;
            var op = outPts[0];
            while (op.Idx == 0 && op.Next != op.Prev)
            {
                if (PointsAreClose(op.Pt, op.Prev.Pt, distSqrd))
                {
                    op = ExcludeOp(op);
                    cnt--;
                }
                else if (PointsAreClose(op.Prev.Pt, op.Next.Pt, distSqrd))
                {
                    ExcludeOp(op.Next);
                    op = ExcludeOp(op);
                    cnt -= 2;
                }
                else if (SlopesNearCollinear(op.Prev.Pt, op.Pt, op.Next.Pt, distSqrd))
                {
                    op = ExcludeOp(op);
                    cnt--;
                }
                else
                {
                    op.Idx = 1;
                    op = op.Next;
                }
            }

            if (cnt < 3) cnt = 0;
            var result = new Path(cnt);
            for (var i = 0; i < cnt; ++i)
            {
                result.Add(op.Pt);
                op = op.Next;
            }
            return result;
        }
        //------------------------------------------------------------------------------

        internal static Paths CleanPolygons(Paths polys,
            double distance = 1.415)
        {
            var result = new Paths(polys.Count);
            result.AddRange(polys.Select(t => CleanPolygon(t, distance)));
            return result;
        }
        //------------------------------------------------------------------------------

        internal static Paths Minkowski(Path pattern, Path path, bool isSum, bool isClosed)
        {
            var delta = (isClosed ? 1 : 0);
            var polyCnt = pattern.Count;
            var pathCnt = path.Count;
            var result = new Paths(pathCnt);
            if (isSum)
                for (var i = 0; i < pathCnt; i++)
                {
                    var p = new Path(polyCnt);
                    var i1 = i;
                    p.AddRange(pattern.Select(ip => new IntPoint(path[i1].X + ip.X, path[i1].Y + ip.Y)));
                    result.Add(p);
                }
            else
                for (var i = 0; i < pathCnt; i++)
                {
                    var p = new Path(polyCnt);
                    var i1 = i;
                    p.AddRange(pattern.Select(ip => new IntPoint(path[i1].X - ip.X, path[i1].Y - ip.Y)));
                    result.Add(p);
                }

            var quads = new Paths((pathCnt + delta) * (polyCnt + 1));
            for (var i = 0; i < pathCnt - 1 + delta; i++)
                for (var j = 0; j < polyCnt; j++)
                {
                    var quad = new Path(4)
                    {
                        result[i%pathCnt][j%polyCnt],
                        result[(i + 1)%pathCnt][j%polyCnt],
                        result[(i + 1)%pathCnt][(j + 1)%polyCnt],
                        result[i%pathCnt][(j + 1)%polyCnt]
                    };
                    if (!Orientation(quad)) quad.Reverse();
                    quads.Add(quad);
                }
            return quads;
        }
        //------------------------------------------------------------------------------

        internal static Paths MinkowskiSum(Path pattern, Path path, bool pathIsClosed)
        {
            var paths = Minkowski(pattern, path, true, pathIsClosed);
            var clipper = new Clipper();
            clipper.AddPaths(paths, PolyType.Subject, true);
            clipper.Execute(ClipType.Union, paths, PolyFillType.NonZero, PolyFillType.NonZero);
            return paths;
        }
        //------------------------------------------------------------------------------

        private static Path TranslatePath(Path path, IntPoint delta)
        {
            var outPath = new Path(path.Count);
            for (var i = 0; i < path.Count; i++)
                outPath.Add(new IntPoint(path[i].X + delta.X, path[i].Y + delta.Y));
            return outPath;
        }
        //------------------------------------------------------------------------------

        internal static Paths MinkowskiSum(Path pattern, Paths paths, bool pathIsClosed)
        {
            var solution = new Paths();
            var clipper = new Clipper();
            foreach (var p in paths)
            {
                var tmp = Minkowski(pattern, p, true, pathIsClosed);
                clipper.AddPaths(tmp, PolyType.Subject, true);
                if (!pathIsClosed) continue;
                var path = TranslatePath(p, pattern[0]);
                clipper.AddPath(path, PolyType.Clip, true);
            }
            clipper.Execute(ClipType.Union, solution,
              PolyFillType.NonZero, PolyFillType.NonZero);
            return solution;
        }
        //------------------------------------------------------------------------------

        internal static Paths MinkowskiDiff(Path poly1, Path poly2)
        {
            var paths = Minkowski(poly1, poly2, false, true);
            var clipper = new Clipper();
            clipper.AddPaths(paths, PolyType.Subject, true);
            clipper.Execute(ClipType.Union, paths, PolyFillType.NonZero, PolyFillType.NonZero);
            return paths;
        }
        //------------------------------------------------------------------------------

        internal enum NodeType { NodeTypeAny, NodeTypeOpen, NodeTypeClosed };

        internal static Paths PolyTreeToPaths(PolyTree polytree)
        {
            var result = new Paths {Capacity = polytree.Total};
            AddPolyNodeToPaths(polytree, NodeType.NodeTypeAny, result);
            return result;
        }
        //------------------------------------------------------------------------------

        internal static void AddPolyNodeToPaths(PolyNode polynode, NodeType nt, Paths paths)
        {
            bool match = true;
            switch (nt)
            {
                case NodeType.NodeTypeOpen: return;
                case NodeType.NodeTypeClosed: match = !polynode.IsOpen; break;
            }

            if (polynode.MPolygon.Count > 0 && match)
                paths.Add(polynode.MPolygon);
            foreach (PolyNode pn in polynode.Childs)
                AddPolyNodeToPaths(pn, nt, paths);
        }
        //------------------------------------------------------------------------------

        internal static Paths OpenPathsFromPolyTree(PolyTree polytree)
        {
            var result = new Paths {Capacity = polytree.ChildCount};
            for (var i = 0; i < polytree.ChildCount; i++)
                if (polytree.Childs[i].IsOpen)
                    result.Add(polytree.Childs[i].MPolygon);
            return result;
        }
        //------------------------------------------------------------------------------

        internal static Paths ClosedPathsFromPolyTree(PolyTree polytree)
        {
            var result = new Paths {Capacity = polytree.Total};
            AddPolyNodeToPaths(polytree, NodeType.NodeTypeClosed, result);
            return result;
        }
        //------------------------------------------------------------------------------

    } //end Clipper

    #endregion 

    #region Clipper Offset
    internal class ClipperOffset
    {
        private Paths m_destPolys;
        private Path m_srcPoly;
        private Path m_destPoly;
        private List<DoublePoint> m_normals = new List<DoublePoint>();
        private double m_delta, m_sinA, m_sin, m_cos;
        private double m_miterLim, m_StepsPerRad;

        private IntPoint m_lowest;
        private PolyNode m_polyNodes = new PolyNode();

        internal double ArcTolerance { get; set; }
        internal double MiterLimit { get; set; }

        private const double two_pi = Math.PI * 2;
        private const double def_arc_tolerance = 0.25;

        internal ClipperOffset(
          double miterLimit = 2.0, double arcTolerance = def_arc_tolerance)
        {
            MiterLimit = miterLimit;
            ArcTolerance = arcTolerance;
            m_lowest.X = -1;
        }
        //------------------------------------------------------------------------------

        internal void Clear()
        {
            m_polyNodes.Childs.Clear();
            m_lowest.X = -1;
        }
        //------------------------------------------------------------------------------

        internal static long Round(double value)
        {
            return value < 0 ? (long)(value - 0.5) : (long)(value + 0.5);
        }
        //------------------------------------------------------------------------------

        internal void AddPath(Path path, JoinType joinType, EndType endType)
        {
            int highI = path.Count - 1;
            if (highI < 0) return;
            PolyNode newNode = new PolyNode();
            newNode.MJointype = joinType;
            newNode.MEndtype = endType;

            //strip duplicate points from path and also get index to the lowest point ...
            if (endType == EndType.ClosedLine || endType == EndType.ClosedPolygon)
                while (highI > 0 && path[0] == path[highI]) highI--;
            newNode.MPolygon.Capacity = highI + 1;
            newNode.MPolygon.Add(path[0]);
            int j = 0, k = 0;
            for (int i = 1; i <= highI; i++)
                if (newNode.MPolygon[j] != path[i])
                {
                    j++;
                    newNode.MPolygon.Add(path[i]);
                    if (path[i].Y > newNode.MPolygon[k].Y ||
                      (path[i].Y == newNode.MPolygon[k].Y &&
                      path[i].X < newNode.MPolygon[k].X)) k = j;
                }
            if (endType == EndType.ClosedPolygon && j < 2) return;

            m_polyNodes.AddChild(newNode);

            //if this path's lowest pt is lower than all the others then update m_lowest
            if (endType != EndType.ClosedPolygon) return;
            if (m_lowest.X < 0)
                m_lowest = new IntPoint(m_polyNodes.ChildCount - 1, k);
            else
            {
                IntPoint ip = m_polyNodes.Childs[(int)m_lowest.X].MPolygon[(int)m_lowest.Y];
                if (newNode.MPolygon[k].Y > ip.Y ||
                  (newNode.MPolygon[k].Y == ip.Y &&
                  newNode.MPolygon[k].X < ip.X))
                    m_lowest = new IntPoint(m_polyNodes.ChildCount - 1, k);
            }
        }
        //------------------------------------------------------------------------------

        internal void AddPaths(Paths paths, JoinType joinType, EndType endType)
        {
            foreach (Path p in paths)
                AddPath(p, joinType, endType);
        }
        //------------------------------------------------------------------------------

        private void FixOrientations()
        {
            //fixup orientations of all closed paths if the orientation of the
            //closed path with the lowermost vertex is wrong ...
            if (m_lowest.X >= 0 &&
              !Clipper.Orientation(m_polyNodes.Childs[(int)m_lowest.X].MPolygon))
            {
                for (int i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    PolyNode node = m_polyNodes.Childs[i];
                    if (node.MEndtype == EndType.ClosedPolygon ||
                      (node.MEndtype == EndType.ClosedLine &&
                      Clipper.Orientation(node.MPolygon)))
                        node.MPolygon.Reverse();
                }
            }
            else
            {
                for (int i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    PolyNode node = m_polyNodes.Childs[i];
                    if (node.MEndtype == EndType.ClosedLine &&
                      !Clipper.Orientation(node.MPolygon))
                        node.MPolygon.Reverse();
                }
            }
        }
        //------------------------------------------------------------------------------

        internal static DoublePoint GetUnitNormal(IntPoint pt1, IntPoint pt2)
        {
            double dx = (pt2.X - pt1.X);
            double dy = (pt2.Y - pt1.Y);
            if (dx.IsNegligible() && dx.IsNegligible()) return new DoublePoint();

            double f = 1 * 1.0 / Math.Sqrt(dx * dx + dy * dy);
            dx *= f;
            dy *= f;

            return new DoublePoint(dy, -dx);
        }
        //------------------------------------------------------------------------------

        private void DoOffset(double delta)
        {
            m_destPolys = new Paths();
            m_delta = delta;

            //if Zero offset, just copy any CLOSED polygons to m_p and return ...
            if (ClipperBase.near_zero(delta))
            {
                m_destPolys.Capacity = m_polyNodes.ChildCount;
                for (int i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    PolyNode node = m_polyNodes.Childs[i];
                    if (node.MEndtype == EndType.ClosedPolygon)
                        m_destPolys.Add(node.MPolygon);
                }
                return;
            }

            //see offset_triginometry3.svg in the documentation folder ...
            if (MiterLimit > 2) m_miterLim = 2 / (MiterLimit * MiterLimit);
            else m_miterLim = 0.5;

            double y;
            if (ArcTolerance <= 0.0)
                y = def_arc_tolerance;
            else if (ArcTolerance > Math.Abs(delta) * def_arc_tolerance)
                y = Math.Abs(delta) * def_arc_tolerance;
            else
                y = ArcTolerance;
            //see offset_triginometry2.svg in the documentation folder ...
            double steps = Math.PI / Math.Acos(1 - y / Math.Abs(delta));
            m_sin = Math.Sin(two_pi / steps);
            m_cos = Math.Cos(two_pi / steps);
            m_StepsPerRad = steps / two_pi;
            if (delta < 0.0) m_sin = -m_sin;

            m_destPolys.Capacity = m_polyNodes.ChildCount * 2;
            for (int i = 0; i < m_polyNodes.ChildCount; i++)
            {
                PolyNode node = m_polyNodes.Childs[i];
                m_srcPoly = node.MPolygon;

                int len = m_srcPoly.Count;

                if (len == 0 || (delta <= 0 && (len < 3 ||
                  node.MEndtype != EndType.ClosedPolygon)))
                    continue;

                m_destPoly = new Path();

                if (len == 1)
                {
                    if (node.MJointype == JoinType.Round)
                    {
                        double X = 1.0, Y = 0.0;
                        for (int j = 1; j <= steps; j++)
                        {
                            m_destPoly.Add(new IntPoint(
                              Round(m_srcPoly[0].X + X * delta),
                              Round(m_srcPoly[0].Y + Y * delta)));
                            double X2 = X;
                            X = X * m_cos - m_sin * Y;
                            Y = X2 * m_sin + Y * m_cos;
                        }
                    }
                    else
                    {
                        double X = -1.0, Y = -1.0;
                        for (int j = 0; j < 4; ++j)
                        {
                            m_destPoly.Add(new IntPoint(
                              Round(m_srcPoly[0].X + X * delta),
                              Round(m_srcPoly[0].Y + Y * delta)));
                            if (X < 0) X = 1;
                            else if (Y < 0) Y = 1;
                            else X = -1;
                        }
                    }
                    m_destPolys.Add(m_destPoly);
                    continue;
                }

                //build m_normals ...
                m_normals.Clear();
                m_normals.Capacity = len;
                for (int j = 0; j < len - 1; j++)
                    m_normals.Add(GetUnitNormal(m_srcPoly[j], m_srcPoly[j + 1]));
                if (node.MEndtype == EndType.ClosedLine ||
                  node.MEndtype == EndType.ClosedPolygon)
                    m_normals.Add(GetUnitNormal(m_srcPoly[len - 1], m_srcPoly[0]));
                else
                    m_normals.Add(new DoublePoint(m_normals[len - 2]));

                if (node.MEndtype == EndType.ClosedPolygon)
                {
                    int k = len - 1;
                    for (int j = 0; j < len; j++)
                        OffsetPoint(j, ref k, node.MJointype);
                    m_destPolys.Add(m_destPoly);
                }
                else if (node.MEndtype == EndType.ClosedLine)
                {
                    var k = len - 1;
                    for (var j = 0; j < len; j++)
                        OffsetPoint(j, ref k, node.MJointype);
                    m_destPolys.Add(m_destPoly);
                    m_destPoly = new Path();
                    //re-build m_normals ...
                    var n = m_normals[len - 1];
                    for (var j = len - 1; j > 0; j--)
                        m_normals[j] = new DoublePoint(-m_normals[j - 1].X, -m_normals[j - 1].Y);
                    m_normals[0] = new DoublePoint(-n.X, -n.Y);
                    k = 0;
                    for (var j = len - 1; j >= 0; j--)
                        OffsetPoint(j, ref k, node.MJointype);
                    m_destPolys.Add(m_destPoly);
                }
                else
                {
                    var k = 0;
                    for (var j = 1; j < len - 1; ++j)
                        OffsetPoint(j, ref k, node.MJointype);

                    IntPoint pt1;
                    if (node.MEndtype == EndType.OpenButt)
                    {
                        var j = len - 1;
                        pt1 = new IntPoint(Round(m_srcPoly[j].X + m_normals[j].X *
                          delta), Round(m_srcPoly[j].Y + m_normals[j].Y * delta));
                        m_destPoly.Add(pt1);
                        pt1 = new IntPoint(Round(m_srcPoly[j].X - m_normals[j].X *
                          delta), Round(m_srcPoly[j].Y - m_normals[j].Y * delta));
                        m_destPoly.Add(pt1);
                    }
                    else
                    {
                        var j = len - 1;
                        k = len - 2;
                        m_sinA = 0;
                        m_normals[j] = new DoublePoint(-m_normals[j].X, -m_normals[j].Y);
                        if (node.MEndtype == EndType.OpenSquare)
                            DoSquare(j, k);
                        else
                            DoRound(j, k);
                    }

                    //re-build m_normals ...
                    for (var j = len - 1; j > 0; j--)
                        m_normals[j] = new DoublePoint(-m_normals[j - 1].X, -m_normals[j - 1].Y);

                    m_normals[0] = new DoublePoint(-m_normals[1].X, -m_normals[1].Y);

                    k = len - 1;
                    for (var j = k - 1; j > 0; --j)
                        OffsetPoint(j, ref k, node.MJointype);

                    if (node.MEndtype == EndType.OpenButt)
                    {
                        pt1 = new IntPoint(Round(m_srcPoly[0].X - m_normals[0].X * delta),
                          Round(m_srcPoly[0].Y - m_normals[0].Y * delta));
                        m_destPoly.Add(pt1);
                        pt1 = new IntPoint(Round(m_srcPoly[0].X + m_normals[0].X * delta),
                          Round(m_srcPoly[0].Y + m_normals[0].Y * delta));
                        m_destPoly.Add(pt1);
                    }
                    else
                    {
                        m_sinA = 0;
                        if (node.MEndtype == EndType.OpenSquare)
                            DoSquare(0, 1);
                        else
                            DoRound(0, 1);
                    }
                    m_destPolys.Add(m_destPoly);
                }
            }
        }
        //------------------------------------------------------------------------------

        internal void Execute(ref Paths solution, double delta)
        {
            solution.Clear();
            FixOrientations();
            DoOffset(delta);
            //now clean up 'corners' ...
            var clipper = new Clipper();
            clipper.AddPaths(m_destPolys, PolyType.Subject, true);
            if (delta > 0)
            {
                clipper.Execute(ClipType.Union, solution,
                  PolyFillType.Positive, PolyFillType.Positive);
            }
            else
            {
                var r = ClipperBase.GetBounds(m_destPolys);
                var outerPath = new Path(4)
                {
                    new IntPoint(r.left - 10, r.bottom + 10),
                    new IntPoint(r.right + 10, r.bottom + 10),
                    new IntPoint(r.right + 10, r.top - 10),
                    new IntPoint(r.left - 10, r.top - 10)
                };


                clipper.AddPath(outerPath, PolyType.Subject, true);
                clipper.ReverseSolution = true;
                clipper.Execute(ClipType.Union, solution, PolyFillType.Negative, PolyFillType.Negative);
                if (solution.Count > 0) solution.RemoveAt(0);
            }
        }
        //------------------------------------------------------------------------------

        internal void Execute(ref PolyTree solution, double delta)
        {
            solution.Clear();
            FixOrientations();
            DoOffset(delta);

            //now clean up 'corners' ...
            var clipper = new Clipper();
            clipper.AddPaths(m_destPolys, PolyType.Subject, true);
            if (delta > 0)
            {
                clipper.Execute(ClipType.Union, solution,
                  PolyFillType.Positive, PolyFillType.Positive);
            }
            else
            {
                var r = ClipperBase.GetBounds(m_destPolys);
                var outerPath = new Path(4)
                {
                    new IntPoint(r.left - 10, r.bottom + 10),
                    new IntPoint(r.right + 10, r.bottom + 10),
                    new IntPoint(r.right + 10, r.top - 10),
                    new IntPoint(r.left - 10, r.top - 10)
                };


                clipper.AddPath(outerPath, PolyType.Subject, true);
                clipper.ReverseSolution = true;
                clipper.Execute(ClipType.Union, solution, PolyFillType.Negative, PolyFillType.Negative);
                //remove the outer PolyNode rectangle ...
                if (solution.ChildCount == 1 && solution.Childs[0].ChildCount > 0)
                {
                    PolyNode outerNode = solution.Childs[0];
                    solution.Childs.Capacity = outerNode.ChildCount;
                    solution.Childs[0] = outerNode.Childs[0];
                    solution.Childs[0].MParent = solution;
                    for (int i = 1; i < outerNode.ChildCount; i++)
                        solution.AddChild(outerNode.Childs[i]);
                }
                else
                    solution.Clear();
            }
        }
        //------------------------------------------------------------------------------

        void OffsetPoint(int j, ref int k, JoinType jointype)
        {
            //cross product ...
            m_sinA = (m_normals[k].X * m_normals[j].Y - m_normals[j].X * m_normals[k].Y);

            if (Math.Abs(m_sinA * m_delta) < 1.0)
            {
                //dot product ...
                double cosA = (m_normals[k].X * m_normals[j].X + m_normals[j].Y * m_normals[k].Y);
                if (cosA > 0) // angle ==> 0 degrees
                {
                    m_destPoly.Add(new IntPoint(Round(m_srcPoly[j].X + m_normals[k].X * m_delta),
                      Round(m_srcPoly[j].Y + m_normals[k].Y * m_delta)));
                    return;
                }
                //else angle ==> 180 degrees   
            }
            else if (m_sinA > 1.0) m_sinA = 1.0;
            else if (m_sinA < -1.0) m_sinA = -1.0;

            if (m_sinA * m_delta < 0)
            {
                m_destPoly.Add(new IntPoint(Round(m_srcPoly[j].X + m_normals[k].X * m_delta),
                  Round(m_srcPoly[j].Y + m_normals[k].Y * m_delta)));
                m_destPoly.Add(m_srcPoly[j]);
                m_destPoly.Add(new IntPoint(Round(m_srcPoly[j].X + m_normals[j].X * m_delta),
                  Round(m_srcPoly[j].Y + m_normals[j].Y * m_delta)));
            }
            else
                switch (jointype)
                {
                    case JoinType.Miter:
                        {
                            double r = 1 + (m_normals[j].X * m_normals[k].X +
                              m_normals[j].Y * m_normals[k].Y);
                            if (r >= m_miterLim) DoMiter(j, k, r); else DoSquare(j, k);
                            break;
                        }
                    case JoinType.Square: DoSquare(j, k); break;
                    case JoinType.Round: DoRound(j, k); break;
                }
            k = j;
        }
        //------------------------------------------------------------------------------

        internal void DoSquare(int j, int k)
        {
            double dx = Math.Tan(Math.Atan2(m_sinA,
                m_normals[k].X * m_normals[j].X + m_normals[k].Y * m_normals[j].Y) / 4);
            m_destPoly.Add(new IntPoint(
                Round(m_srcPoly[j].X + m_delta * (m_normals[k].X - m_normals[k].Y * dx)),
                Round(m_srcPoly[j].Y + m_delta * (m_normals[k].Y + m_normals[k].X * dx))));
            m_destPoly.Add(new IntPoint(
                Round(m_srcPoly[j].X + m_delta * (m_normals[j].X + m_normals[j].Y * dx)),
                Round(m_srcPoly[j].Y + m_delta * (m_normals[j].Y - m_normals[j].X * dx))));
        }
        //------------------------------------------------------------------------------

        internal void DoMiter(int j, int k, double r)
        {
            double q = m_delta / r;
            m_destPoly.Add(new IntPoint(Round(m_srcPoly[j].X + (m_normals[k].X + m_normals[j].X) * q),
                Round(m_srcPoly[j].Y + (m_normals[k].Y + m_normals[j].Y) * q)));
        }
        //------------------------------------------------------------------------------

        internal void DoRound(int j, int k)
        {
            var a = Math.Atan2(m_sinA,
            m_normals[k].X * m_normals[j].X + m_normals[k].Y * m_normals[j].Y);
            var steps = Math.Max((int)Round(m_StepsPerRad * Math.Abs(a)), 1);

            double x = m_normals[k].X, y = m_normals[k].Y;
            for (var i = 0; i < steps; ++i)
            {
                m_destPoly.Add(new IntPoint(
                    Round(m_srcPoly[j].X + x * m_delta),
                    Round(m_srcPoly[j].Y + y * m_delta)));
                var X2 = x;
                x = x * m_cos - m_sin * y;
                y = X2 * m_sin + y * m_cos;
            }
            m_destPoly.Add(new IntPoint(
            Round(m_srcPoly[j].X + m_normals[j].X * m_delta),
            Round(m_srcPoly[j].Y + m_normals[j].Y * m_delta)));
        }
        //------------------------------------------------------------------------------
    }
    #endregion

    internal class ClipperException : Exception
    {
        internal ClipperException(string description) : base(description) { }
    }
} //end ClipperLib namespace
