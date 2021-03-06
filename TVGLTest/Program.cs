﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StarMathLib;
using TVGL;
using TVGL.Boolean_Operations;
using TVGL.Clipper;
using TVGL.IOFunctions;

namespace TVGL_Test
{
    using Path = List<Point>;
    using Paths = List<List<Point>>;

    internal class Program
    {
        private static readonly string[] FileNames = {
        "../../../TestFiles/ABF.ply",
       // "../../../TestFiles/Beam_Boss.STL",
       // //"../../../TestFiles/bigmotor.amf",
       // //"../../../TestFiles/DxTopLevelPart2.shell",
       // //"../../../TestFiles/Candy.shell",
       // //"../../../TestFiles/amf_Cube.amf",
       // //"../../../TestFiles/train.3mf",
       // //"../../../TestFiles/Castle.3mf",
       // //"../../../TestFiles/Raspberry Pi Case.3mf",
       ////"../../../TestFiles/shark.ply",
     // "../../../TestFiles/bunnySmall.ply",
       // "../../../TestFiles/cube.ply",
       // //"../../../TestFiles/airplane.ply",
       // "../../../TestFiles/TXT - G5 support de carrosserie-1.STL.ply",
        "../../../TestFiles/Tetrahedron.STL",
       // "../../../TestFiles/off_axis_box.STL",
       // "../../../TestFiles/Wedge.STL",
       // "../../../TestFiles/Mic_Holder_SW.stl",
       // "../../../TestFiles/Mic_Holder_JR.stl",
       // "../../../TestFiles/3_bananas.amf",
       // "../../../TestFiles/drillparts.amf",  //Edge/face relationship contains errors
       // "../../../TestFiles/wrenchsns.amf", //convex hull edge contains a concave edge outside of tolerance
       // "../../../TestFiles/Rook.amf",
        //"../../../TestFiles/hdodec.off",
        //"../../../TestFiles/tref.off",
        "../../../TestFiles/mushroom.off",
        "../../../TestFiles/vertcube.off",
        "../../../TestFiles/trapezoid.4d.off",
        "../../../TestFiles/ABF.STL",
        "../../../TestFiles/Pump-1repair.STL",
        "../../../TestFiles/Pump-1.STL",
        "../../../TestFiles/Beam_Clean.STL",
        "../../../TestFiles/piston.stl",
        "../../../TestFiles/Z682.stl",
        "../../../TestFiles/sth2.stl",
        "../../../TestFiles/Cuboide.stl", //Note that this is an assembly 
        "../../../TestFiles/new/5.STL",
       "../../../TestFiles/new/2.stl", //Note that this is an assembly 
        "../../../TestFiles/new/6.stl", //Note that this is an assembly  //breaks in slice at 1/2 y direction
       "../../../TestFiles/new/4.stl", //breaks because one of its faces has no normal
        "../../../TestFiles/radiobox.stl",
        "../../../TestFiles/brace.stl",  //Convex hull fails in MIconvexHull
        "../../../TestFiles/G0.stl",
        "../../../TestFiles/GKJ0.stl",
        "../../../TestFiles/testblock2.stl",
        "../../../TestFiles/Z665.stl",
        "../../../TestFiles/Casing.stl", //breaks because one of its faces has no normal
        "../../../TestFiles/mendel_extruder.stl",
        "../../../TestFiles/Aerospace_Beam.STL",
       "../../../TestFiles/MV-Test files/holding-device.STL",
       "../../../TestFiles/MV-Test files/gear.STL"
        };

        [STAThread]
        private static void Main(string[] args)
        {
            //Difference2();
            var writer = new TextWriterTraceListener(Console.Out);
            Debug.Listeners.Add(writer);
            TVGL.Message.Verbosity = VerbosityLevels.OnlyCritical;
            var dir = new DirectoryInfo("../../../TestFiles");
            var fileNames = dir.GetFiles("*.stl");
            for (var i = 0; i < FileNames.Count(); i++)
            {
                var filename = FileNames[i];//.FullName;
                Console.WriteLine("Attempting: " + filename);
                Stream fileStream;
                List<TessellatedSolid> ts;
                using (fileStream = File.OpenRead(filename))
                    ts = IO.Open(fileStream, filename);
                filename += "1.ply";
                using (fileStream = File.OpenWrite(filename))
                    IO.Save(fileStream, ts, FileType.PLY_Binary);
                using (fileStream = File.OpenRead(filename))
                    ts = IO.Open(fileStream, filename);


                //TestPolygon(ts[0]);

                Presenter.ShowAndHang(ts);
              //  TestSilhouette(ts[0]);
                //TestAdditiveVolumeEstimate(ts[0]);
            }

            Console.WriteLine("Completed.");
            //  Console.ReadKey();
        }



        public static void TestAdditiveVolumeEstimate(TessellatedSolid ts)
        {
            List<List<List<Point>>> outputData;
            var volume = AreaDecomposition.AdditiveVolume(ts, new[] {-1.0, 0.0, 0.0}, 0.1, 0.0, out outputData);
            //foreach (var output in data)
            //{
            //    Presenter.ShowAndHang(output);
            //}
        }
        public static void TestSilhouette(TessellatedSolid ts)
        {
            var silhouette = TVGL.Silhouette.Run(ts, new[] {0.5, 0.0, 0.5});
            Presenter.ShowAndHang(silhouette);
        }

        public static void Difference2()
        {
            var subject = new Paths();
            var subject2 = new Paths();
            var clip = new Paths();
            var solution = new Paths();
            var polytree = new PolyTree();
            var clipper = new Clipper();

            PolyFillType fillMethod = PolyFillType.Positive;
            const int scalingFactor = 1000;
            int[] ints1 = { -103, -219, -103, -136, -115, -136 }; //CCW
            int[] ints2 = { -110, -155, -110, -174, -70, -174 }; //CCW

            subject.Add(MakePolygonFromInts(ints1, scalingFactor));
            clip.Add(MakePolygonFromInts(ints2, scalingFactor));

            //ShowPathListsAsDifferentColors(new List<List<Path>>() { subject, clip }, scalingFactor);

            clipper.StrictlySimple = true;
            clipper.AddPaths(subject, PolyType.Subject, true);
            clipper.AddPaths(clip, PolyType.Clip, true);

            var result = clipper.Execute(ClipType.Union, solution, fillMethod, fillMethod);
            //ShowPaths(solution, scalingFactor);
            Assert.That(result, Is.True);
            Assert.That(solution.Count, Is.EqualTo(1));

            result = clipper.Execute(ClipType.Difference, solution, fillMethod, fillMethod);
            //ShowPaths(solution, scalingFactor);
            Assert.That(result, Is.True);
            Assert.That(solution.Count, Is.EqualTo(2));

            result = clipper.Execute(ClipType.Intersection, solution, fillMethod, fillMethod);
            //ShowPaths(solution, scalingFactor);
            Assert.That(result, Is.True);
            Assert.That(solution.Count, Is.EqualTo(1));

            result = clipper.Execute(ClipType.Xor, solution, fillMethod, fillMethod);
            //ShowPaths(solution, scalingFactor);
            Assert.That(result, Is.True);
            Assert.That(solution.Count, Is.EqualTo(4));
        }

        private static Path MakePolygonFromInts(int[] ints, double scale = 1.0)
        {
            var polygon = new Path();

            for (var i = 0; i < ints.Length; i += 2)
            {
                polygon.Add(new Point(scale * ints[i], scale * ints[i + 1]));
            }

            return polygon;
        }

        private static void TestPolygon(TessellatedSolid ts)
        {
            ContactData contactData;
            Slice.GetContactData(ts, new Flat(10, new[] { 1.0, 0, 0 }),
                out contactData);
            throw new NotImplementedException();
        }

        private static void TestOBB(string InputDir)
        {
            var di = new DirectoryInfo(InputDir);
            var fis = di.EnumerateFiles();
            var numVertices = new List<int>();
            var data = new List<double[]>();
            foreach (var fileInfo in fis)
            {
                try
                {
                    var ts = IO.Open(fileInfo.Open(FileMode.Open), fileInfo.Name);
                    foreach (var tessellatedSolid in ts)
                    {
                        List<double> times, volumes;
                        MinimumEnclosure.OrientedBoundingBox_Test(tessellatedSolid, out times, out volumes);//, out VolumeData2);
                        data.Add(new[] { tessellatedSolid.ConvexHull.Vertices.Count(), tessellatedSolid.Volume,
                            times[0], times[1],times[2], volumes[0],  volumes[1], volumes[2] });
                    }
                }
                catch { }
            }
            // TVGLTest.ExcelInterface.PlotEachSeriesSeperately(VolumeData1, "Edge", "Angle", "Volume");
            TVGLTest.ExcelInterface.CreateNewGraph(new[] { data }, "", "Methods", "Volume", new[] { "PCA", "ChanTan" });
        }

        private static void TestSimplify(TessellatedSolid ts)
        {
            ts.SimplifyByPercentage(.9);
            Debug.WriteLine("number of vertices = " + ts.NumberOfVertices);
            Debug.WriteLine("number of edges = " + ts.NumberOfEdges);
            Debug.WriteLine("number of faces = " + ts.NumberOfFaces);
            TVGL.Presenter.ShowAndHang(ts);
        }

        //private static void TestClassification(TessellatedSolid ts)
        //{
        //    TesselationToPrimitives.Run(ts);
        //}

        //private static void TestOBB(TessellatedSolid ts, string filename)
        //{
        //    //var obb = MinimumEnclosure.Find_via_PCA_Approach(ts);
        //    //var obb = MinimumEnclosure.Find_via_ChanTan_AABB_Approach(ts);
        //    //var obb = MinimumEnclosure.Find_via_MC_ApproachOne(ts);\
        //    //MiscFunctions.IsConvexHullBroken(ts);
        //    List<List<double[]>> VolumeData1;
        //  //  List<List<double[]>> VolumeData2;
        //    var obb = MinimumEnclosure.OrientedBoundingBox_Test(ts, out VolumeData1);//, out VolumeData2);
        //    //var obb = MinimumEnclosure.Find_via_BM_ApproachOne(ts);
        //    //TVGLTest.ExcelInterface.PlotEachSeriesSeperately(VolumeData1, "Edge", "Angle", "Volume");
        ////   TVGLTest.ExcelInterface.CreateNewGraph(VolumeData1, "", "Methods", "Volume", new []{"PCA", "ChanTan"});
        //}

        private static void TestInsideSolid(TessellatedSolid ts1, TessellatedSolid ts2)
        {
            var now = DateTime.Now;
            Console.WriteLine("start...");
            List<Vertex> insideVertices1;
            List<Vertex> outsideVertices1;
            List<Vertex> insideVertices2;
            List<Vertex> outsideVertices2;
            MiscFunctions.FindSolidIntersections(ts2, ts1, out insideVertices1, out outsideVertices1, out insideVertices2, out outsideVertices2, true);
            //var vertexInQuestion = new Vertex(new[] {0.0, 0.0, 0.0});
            //var isVertexInside = MinimumEnclosure.IsVertexInsideSolid(ts, vertexInQuestion);
            //ToDo: Run test multiple times and look for vertices that change. Get visual and determine cause of error.
            //ToDo: Also, check if boundary function works 
            Console.WriteLine("Is the Solid inside the Solid?");
            Console.WriteLine();
            Console.WriteLine("end...Time Elapsed = " + (DateTime.Now - now));
            Console.ReadLine();
        }


        private static void TestXSections(TessellatedSolid ts)
        {
            var now = DateTime.Now;
            Debug.WriteLine("start...");
            var crossAreas = new double[3][,];
            var maxSlices = 100;
            var delta = Math.Max((ts.Bounds[1][0] - ts.Bounds[0][0]) / maxSlices,
                Math.Max((ts.Bounds[1][1] - ts.Bounds[0][1]) / maxSlices,
                    (ts.Bounds[1][2] - ts.Bounds[0][2]) / maxSlices));
            //Parallel.For(0, 3, i =>
            var greatestDeltas = new List<double>();
            var greatestDeltaLocations = new List<double>();
            var areaData = new List<List<double[]>>();
            for (int i = 0; i < 3; i++)
            {
                //var max = ts.Bounds[1][i];
                //var min = ts.Bounds[0][i];
                //var numSteps = (int)Math.Ceiling((max - min) / delta);
                var coordValues = ts.Vertices.Select(v => v.Position[i]).Distinct().OrderBy(x => x).ToList();
                var numValues = new List<double>();
                var offset = 0.000000001;
                foreach (var coordValue in coordValues)
                {
                    if (coordValues[0] == coordValue)
                    {
                        //Only Add increment forward
                        numValues.Add(coordValue + offset);
                    }
                    else if (coordValues.Last() == coordValue)
                    {
                        //Only Add increment back
                        numValues.Add(coordValue - offset);
                    }
                    else
                    {
                        //Add increment forward and back
                        numValues.Add(coordValue + offset);
                        numValues.Add(coordValue - offset);
                    }
                }
                coordValues = numValues.OrderBy(x => x).ToList();
                var numSteps = coordValues.Count;
                var direction = new double[3];
                direction[i] = 1.0;
                crossAreas[i] = new double[numSteps, 2];
                var greatestDelta = 0.0;
                var previousArea = 0.0;
                var greatestDeltaLocation = 0.0;
                var dataPoints = new List<double[]>();
                for (var j = 0; j < numSteps; j++)
                {
                    var dist = crossAreas[i][j, 0] = coordValues[j];
                    //Console.WriteLine("slice at Coord " + i + " at " + coordValues[j]);
                    var newArea = 0.0;// Slice.DefineContact(ts, new Flat(dist, direction), false).Area;
                    crossAreas[i][j, 1] = newArea;
                    if (j > 0 && Math.Abs(newArea - previousArea) > greatestDelta)
                    {
                        greatestDelta = Math.Abs(newArea - previousArea);
                        greatestDeltaLocation = dist;
                    }
                    var dataPoint = new double[] { dist, newArea };
                    dataPoints.Add(dataPoint);
                    previousArea = newArea;
                }
                areaData.Add(dataPoints);
                greatestDeltas.Add(greatestDelta);
                greatestDeltaLocations.Add(greatestDeltaLocation);
            }//);
            TVGLTest.ExcelInterface.CreateNewGraph(areaData, "Area Decomposition", "Distance From Origin", "Area");
            Debug.WriteLine("end...Time Elapsed = " + (DateTime.Now - now));

            //Console.ReadKey();
            //for (var i = 0; i < 3; i++)
            //{
            //    Debug.WriteLine("\nfor direction " + i);
            //    for (var j = 0; j < crossAreas[i].GetLength(0); j++)
            //    {
            //        Debug.WriteLine(crossAreas[i][j, 0] + ", " + crossAreas[i][j, 1]);
            //    }
            //}
        }
    }
}