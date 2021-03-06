﻿// ***********************************************************************
// Assembly         : TessellationAndVoxelizationGeometryLibrary
// Author           : Design Engineering Lab
// Created          : 04-18-2016
//
// Last Modified By : Design Engineering Lab
// Last Modified On : 05-25-2016
// ***********************************************************************
// <copyright file="AreaDecomposition.cs" company="Design Engineering Lab">
//     Copyright ©  2014
// </copyright>
// <summary></summary>
// ***********************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StarMathLib;
using TVGL;

namespace TVGL
{
    /// <summary>
    ///     Outputs cross sectional area along a given axis
    /// </summary>
    public static class AreaDecomposition
    {
        /// <summary>
        ///     Runs the specified ts.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="stepSize">Size of the step.</param>
        /// <param name="minOffset">The minimum offset.</param>
        /// <param name="ignoreNegativeSpace">if set to <c>true</c> [ignore negative space].</param>
        /// <param name="convexHull2DDecompositon">if set to <c>true</c> [convex hull2 d decompositon].</param>
        /// <param name="boundingRectangleArea">if set to <c>true</c> [bounding rectangle area].</param>
        /// <returns>List&lt;System.Double[]&gt;.</returns>
        public static List<double[]> Run(TessellatedSolid ts, double[] axis, double stepSize,
            double minOffset = double.NaN, bool ignoreNegativeSpace = false, bool convexHull2DDecompositon = false,
            bool boundingRectangleArea = false)
        {
            List<double[]> pointsOfInterestForFeasability;
            const double maxArea = double.PositiveInfinity;
            return Run(ts, axis, out pointsOfInterestForFeasability, maxArea, stepSize, minOffset, ignoreNegativeSpace,
                convexHull2DDecompositon, boundingRectangleArea);
        }

        /// <summary>
        ///     Runs the specified ts.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="pointsOfInterestForFeasability">The points of interest for feasability.</param>
        /// <param name="maxArea">The maximum area.</param>
        /// <param name="stepSize">Size of the step.</param>
        /// <param name="minOffset">The minimum offset.</param>
        /// <param name="ignoreNegativeSpace">if set to <c>true</c> [ignore negative space].</param>
        /// <param name="convexHull2DDecompositon">if set to <c>true</c> [convex hull2 d decompositon].</param>
        /// <param name="boundingRectangleArea">if set to <c>true</c> [bounding rectangle area].</param>
        /// <returns>List&lt;System.Double[]&gt;.</returns>
        /// <exception cref="Exception">Pick one or the other. Can't do both at the same time</exception>
        public static List<double[]> Run(TessellatedSolid ts, double[] axis,
            out List<double[]> pointsOfInterestForFeasability, double maxArea, double stepSize,
            double minOffset = double.NaN, bool ignoreNegativeSpace = false, bool convexHull2DDecompositon = false,
            bool boundingRectangleArea = false)
        {
            //individualFaceAreas = new List<List<double[]>>(); //Plot changes for the area of each flat that makes up a slice. (e.g. 2 positive loop areas)
            if (convexHull2DDecompositon && boundingRectangleArea)
                throw new Exception("Pick one or the other. Can't do both at the same time");
            pointsOfInterestForFeasability = new List<double[]>();
            var outputData = new List<double[]>();
            if (double.IsNaN(minOffset)) minOffset = Math.Sqrt(ts.SameTolerance);
            if (stepSize <= minOffset * 2)
            {
                //"step size must be at least 2x as large as the min offset");
                //Change it rather that throwing an exception
                stepSize = minOffset * 2 + ts.SameTolerance;
            }
            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Vertex> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { axis }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousDistanceAlongAxis = axis.dotProduct(sortedVertices[0].Position); //This value can be negative
            var previousVertexDistance = previousDistanceAlongAxis;
            foreach (var vertex in sortedVertices)
            {
                var distanceAlongAxis = axis.dotProduct(vertex.Position); //This value can be negative
                var difference1 = distanceAlongAxis - previousDistanceAlongAxis;
                var difference2 = distanceAlongAxis - previousVertexDistance;
                if (difference2 > minOffset && difference1 > stepSize)
                {
                    //Determine cross sectional area for section right after previous vertex
                    var distance = previousVertexDistance + minOffset; //X value (distance along axis) 
                    var cuttingPlane = new Flat(distance, axis);
                    List<List<Edge>> outputEdgeLoops = null;
                    var inputEdgeLoops = new List<List<Edge>>();
                    var area = 0.0;
                    if (convexHull2DDecompositon) area = ConvexHull2DArea(edgeListDictionary, cuttingPlane);
                    else if (boundingRectangleArea) area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                    else
                        area = CrossSectionalArea(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops,
                            ignoreNegativeSpace); //Y value (area)
                    outputData.Add(new[] { distance, area });

                    //If the difference is far enough, add another data point right before the current vertex
                    //Use the vertex loops provided from the first pass above
                    if (difference2 > 3 * minOffset)
                    {
                        var distance2 = distanceAlongAxis - minOffset; //X value (distance along axis) 
                        cuttingPlane = new Flat(distance2, axis);
                        if (convexHull2DDecompositon) area = ConvexHull2DArea(edgeListDictionary, cuttingPlane);
                        else if (boundingRectangleArea) area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                        else
                        {
                            inputEdgeLoops = outputEdgeLoops;
                            area = CrossSectionalArea(edgeListDictionary, cuttingPlane, out outputEdgeLoops,
                                inputEdgeLoops, ignoreNegativeSpace); //Y value (area)
                        }
                        outputData.Add(new[] { distance2, area });
                    }

                    //Update the previous distance used to make a data point
                    previousDistanceAlongAxis = distanceAlongAxis;
                }
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
                //Update the previous distance of the vertex checked
                previousVertexDistance = distanceAlongAxis;
            }
            return outputData;
        }


        /// <summary>
        ///     Runs the rectangle restricted.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="maxArea">The maximum area.</param>
        /// <param name="stepSize">Size of the step.</param>
        /// <param name="minOffset">The minimum offset.</param>
        /// <returns>List&lt;System.Double[]&gt;.</returns>
        public static List<double[]> RunRectangleRestricted(TessellatedSolid ts, double[] axis, double maxArea,
            double stepSize,
            double minOffset = double.NaN)
        {
            //individualFaceAreas = new List<List<double[]>>(); //Plot changes for the area of each flat that makes up a slice. (e.g. 2 positive loop areas)
            var outputData = new List<double[]>();

            if (double.IsNaN(minOffset)) minOffset = Math.Sqrt(ts.SameTolerance);
            if (stepSize <= minOffset * 2)
            {
                //"step size must be at least 2x as large as the min offset");
                //Change it rather that throwing an exception
                stepSize = minOffset * 2 + ts.SameTolerance;
            }
            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Vertex> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { axis }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousDistanceAlongAxis = axis.dotProduct(sortedVertices[0].Position); //This value can be negative
            var previousVertexDistance = previousDistanceAlongAxis;
            foreach (var vertex in sortedVertices)
            {
                var distanceAlongAxis = axis.dotProduct(vertex.Position); //This value can be negative
                var difference1 = distanceAlongAxis - previousDistanceAlongAxis;
                var difference2 = distanceAlongAxis - previousVertexDistance;
                if (difference2 > minOffset && difference1 > stepSize)
                {
                    //Determine cross sectional area for section right after previous vertex
                    var distance = previousVertexDistance + minOffset; //X value (distance along axis) 
                    var cuttingPlane = new Flat(distance, axis);
                    var inputEdgeLoops = new List<List<Edge>>();
                    var area = 0.0;
                    area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                    outputData.Add(new[] { distance, area });

                    //The rate of change of area is not necessarily linear because of the convexHull2D
                    //It would be linear as long as the edges that are causing the convex hull don't change,
                    //but I'm not currently tracking that. Using the vertices wouldn't work because many of the 
                    //edges will need new vertices. 
                    //If the difference is greater than the step size, make another data point
                    var currentDistance = distance + stepSize;
                    while (currentDistance < distanceAlongAxis - stepSize)
                    {
                        cuttingPlane = new Flat(currentDistance, axis);
                        area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                        outputData.Add(new[] { currentDistance, area });
                        currentDistance += stepSize;
                    }


                    //If the difference is far enough, add another data point right before the current vertex
                    //Use the vertex loops provided from the first pass above
                    if (difference2 > 3 * minOffset)
                    {
                        var distance2 = distanceAlongAxis - minOffset; //X value (distance along axis) 
                        cuttingPlane = new Flat(distance2, axis);
                        area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                        outputData.Add(new[] { distance2, area });
                    }

                    //Update the previous distance used to make a data point
                    previousDistanceAlongAxis = distanceAlongAxis;
                }
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
                //Update the previous distance of the vertex checked
                previousVertexDistance = distanceAlongAxis;
            }
            return outputData;
        }

        /// <summary>
        /// The Decomposition Data Class used to store information from A Directional Decomposition
        /// </summary>
        public class DecompositionData
        {
            /// <summary>
            /// A list of the paths that make up the slice of the solid at this distance along this direction
            /// </summary>
            public List<List<Point>> Paths;

            /// <summary>
            /// The distance along this direction
            /// </summary>
            public double DistanceAlongDirection;

            /// <summary>
            /// The Decomposition Data Class used to store information from A Directional Decomposition
            /// </summary>
            /// <param name="paths"></param>
            /// <param name="distanceAlongDirection"></param>
            public DecompositionData(IEnumerable<List<Point>> paths, double distanceAlongDirection)
            {
                Paths = new List<List<Point>>(paths);
                DistanceAlongDirection = distanceAlongDirection;
            }
        }

        /// <summary>
        /// Returns the decomposition data found from each slice of the decomposition. This data is used in other methods.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="direction"></param>
        /// <param name="stepSize"></param>
        /// <param name="minOffset"></param>
        /// <returns></returns>
        public static List<DecompositionData> DirectionalDecomposition(TessellatedSolid ts, double[] direction, double stepSize, double minOffset = double.NaN)
        {
            var outputData = new List<DecompositionData>();
            if (double.IsNaN(minOffset)) minOffset = Math.Sqrt(ts.SameTolerance);
            if (stepSize <= minOffset * 2)
            {
                //"step size must be at least 2x as large as the min offset");
                //Change it rather that throwing an exception
                stepSize = minOffset * 2 + ts.SameTolerance;
            }
            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Vertex> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { direction }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousDistanceAlongAxis = direction.dotProduct(sortedVertices[0].Position); //This value can be negative
            var previousVertexDistance = previousDistanceAlongAxis;
            foreach (var vertex in sortedVertices)
            {
                var distanceAlongAxis = direction.dotProduct(vertex.Position); //This value can be negative
                var difference1 = distanceAlongAxis - previousDistanceAlongAxis;
                var difference2 = distanceAlongAxis - previousVertexDistance;
                if (difference2 > minOffset && difference1 > stepSize)
                {
                    //Determine cross sectional area for section right after previous vertex
                    var distance = previousVertexDistance + minOffset; //X value (distance along axis) 
                    var cuttingPlane = new Flat(distance, direction);
                    List<List<Edge>> outputEdgeLoops;
                    var inputEdgeLoops = new List<List<Edge>>();
                    var current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops);

                    //Get a list of 2D paths from the 3D loops
                    var currentPaths = current3DLoops.Select(cp => MiscFunctions.Get2DProjectionPoints(cp, direction).ToList()).ToList();

                    //Add the data to the output
                    outputData.Add(new DecompositionData(currentPaths, distance));

                    //If the difference is far enough, add another data point right before the current vertex
                    //Use the vertex loops provided from the first pass above
                    if (difference2 > 3 * minOffset)
                    {
                        var distance2 = distanceAlongAxis - minOffset; //X value (distance along axis) 
                        cuttingPlane = new Flat(distance2, direction);
                        current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops);

                        //Get a list of 2D paths from the 3D loops
                        currentPaths = current3DLoops.Select(cp => MiscFunctions.Get2DProjectionPoints(cp, direction).ToList()).ToList();

                        //Add the data to the output
                        outputData.Add(new DecompositionData(currentPaths, distance2));
                    }

                    //Update the previous distance used to make a data point
                    previousDistanceAlongAxis = distanceAlongAxis;
                }
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
                //Update the previous distance of the vertex checked
                previousVertexDistance = distanceAlongAxis;
            }
            return outputData;
        }

        /// <summary>
        /// Returns the decomposition data found from each slice of the decomposition. This data is used in other methods.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="direction"></param>
        /// <param name="stepSize"></param>
        /// <returns></returns>
        public static List<DecompositionData> UniformDirectionalDecomposition(TessellatedSolid ts, double[] direction, double stepSize)
        {
            var outputData = new List<DecompositionData>();

            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Vertex> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { direction }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousDistanceAlongAxis = direction.dotProduct(sortedVertices[0].Position); //This value can be negative
            foreach (var vertex in sortedVertices)
            {
                var distanceAlongAxis = direction.dotProduct(vertex.Position); //This value can be negative
                var difference1 = distanceAlongAxis - previousDistanceAlongAxis;
                //If the vertex is far enough that the next step size is reached, get the cross section.
                var i = 1;
                var distance = previousDistanceAlongAxis;
                while (difference1 > i * stepSize )
                { 
                    //Determine the next step distance
                    distance = previousDistanceAlongAxis + stepSize*i; //X value (distance along axis) 
                    var cuttingPlane = new Flat(distance, direction);
                    List<List<Edge>> outputEdgeLoops;
                    var inputEdgeLoops = new List<List<Edge>>();
                    var current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops);

                    //Get a list of 2D paths from the 3D loops
                    var currentPaths = current3DLoops.Select(cp => MiscFunctions.Get2DProjectionPoints(cp, direction).ToList()).ToList();

                    //Add the data to the output
                    outputData.Add(new DecompositionData(currentPaths, distance));

                    i++;
                }

                //Update the previous distance used to make a data point
                previousDistanceAlongAxis = distance;

                //Update the edge dictionary that is used to determine the 3D loops.
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
            }
            return outputData;
        }

        /// <summary>
        /// Gets the additive volume given a list of decomposition data
        /// </summary>
        /// <param name="decompData"></param>
        /// <param name="additiveAccuracy"></param>
        /// <param name="outputData"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static double AdditiveVolume(List<DecompositionData> decompData, double additiveAccuracy, out List<DecompositionData> outputData )
        {
            outputData = new List<DecompositionData>();
            var previousPolygons = new List<List<Point>>();
            var previousDistance = 0.0;
            var previousArea = 0.0;
            var additiveVolume = 0.0;
            var i = 0;
            foreach (var data in decompData)
            {
                var currentPaths = data.Paths;
                var distance = data.DistanceAlongDirection;

                //Offset if the additive accuracy is significant
                //Since you could offset the wrong direction if the loops are ordered incorrectly, we must first reorder if necessary
                //bool[] isPositive = null;
                //try
                //{
                //    currentPaths = TriangulatePolygon.OrderLoops2D(currentPaths, ref isPositive);
                //}
                //catch
                //{
                    currentPaths = PolygonOperations.UnionEvenOdd(currentPaths);
               // }
                
                var offsetPaths = !additiveAccuracy.IsNegligible() ? PolygonOperations.OffsetSquare(currentPaths, additiveAccuracy) : new List<List<Point>>(currentPaths);
                var simpleOffset = offsetPaths.Select(PolygonOperations.SimplifyFuzzy).ToList();

                //Union this new set of polygons with the previous set.
                if (previousPolygons.Any()) //If not the first iteration
                {
                    previousPolygons = previousPolygons.Select(PolygonOperations.SimplifyFuzzy).ToList();
                    try
                    {
                        currentPaths = new List<List<Point>>(PolygonOperations.Union(previousPolygons, simpleOffset));
                    }
                    catch
                    {
                        var testArea1 = simpleOffset.Sum(p => MiscFunctions.AreaOfPolygon(p));
                        var testArea2 = previousPolygons.Sum(p => MiscFunctions.AreaOfPolygon(p));
                        if (testArea1.IsPracticallySame(testArea2, 0.01))
                        {
                            currentPaths = simpleOffset;
                            //They are probably throwing an error because they are closely overlapping
                        }
                        else
                        {
                            ////Debug Mode
                            //var previousData = outputData.Last();
                            //outputData = new List<DecompositionData>() { previousData, new DecompositionData( currentPaths, distance )};
                            //return 0.0;

                            //Run mode: Use previous path
                            Debug.WriteLine("Union failed and not similar");
                            //
                            currentPaths = outputData.Last().Paths;
                        }
                    }
                }

                //Get the area of this layer
                var area = currentPaths.Sum(p => MiscFunctions.AreaOfPolygon(p));
                if (area < 0)
                {
                    //Rather than throwing an exception, just assume the polygons were the wrong direction      
                    area = -area;
                    Debug.WriteLine("Area for a polygon in the Additive Volume estimate was negative. This means there was an issue with the polygon ordering");
                }

                //This is the first iteration. Add it to the output data.
                if (i == 0)
                {
                    outputData.Add(new DecompositionData(simpleOffset, distance));
                    var area2 = simpleOffset.Sum(p => MiscFunctions.AreaOfPolygon(p));
                    if (area2 < 0)
                    {
                        //Rather than throwing an exception, just assume the polygons were the wrong direction      
                        area2 = -area2;
                        Debug.WriteLine("The first polygon in the Additive Volume estimate was negative. This means there was an issue with the polygon ordering");
                    }
                    additiveVolume += additiveAccuracy*area2;
                }
                
                //Add the volume from this iteration.
                else if (!previousDistance.IsNegligible())
                {
                    var deltaX = Math.Abs(distance - previousDistance);
                    if (area < previousArea * .99)
                    {
                        ////Debug Mode
                        //var previousData = outputData.Last();
                        //outputData = new List<DecompositionData>() { previousData, new DecompositionData( currentPaths, distance )};
                        //return 0.0;

                        //Run Mode: use previous area
                        Debug.WriteLine("Error in your implementation. This should never occur");
                        area = previousArea;
                        
                    }
                    additiveVolume += deltaX * previousArea;
                    outputData.Add(new DecompositionData(currentPaths, distance));

                    //This is the last iteration. Add it to the output data.
                    if (i == decompData.Count - 1)
                    {
                        outputData.Add(new DecompositionData(currentPaths, distance + additiveAccuracy));
                        additiveVolume += additiveAccuracy * area;
                    }
                }
                previousPolygons = currentPaths;
                previousDistance = distance;
                previousArea = area;
                i++;
            }
            return additiveVolume;
        }

        /// <summary>
        /// Returns the additive volume of a solid, with support material and an offset accuracy, Given the direction of printing.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="direction"></param>
        /// <param name="stepSize"></param>
        /// <param name="additiveAccuracy">The additive processes accuracy, used for a Polygon Offset. </param>
        /// <param name="outputData"></param>
        /// <param name="minOffset"></param>
        /// <returns></returns>
        public static double AdditiveVolume(TessellatedSolid ts, double[] direction, double stepSize, double additiveAccuracy, out List<List<List<Point>>> outputData, double minOffset = double.NaN)
        {
            //The idea here is to slice up the solid from top to bottom, along the direction given. 
            //The loop from a pervious iteration is merged with the new loop, to form a larger loop.
            //If an unconnected loop appears, it adds to total area of that slice.
            //1) previousPolygons => Get the loops and area for the first slice. 
            //   previousOffsetArea => Offset all the loops by the additive offset and get the area.
            //2) While not complete, cut at the next depth
            //   get the loops for the next depth (step size).
            //   currentPolygons => Use "UnionPolygons" from Clipper to get the union of positive and negative loops
            //   offsetArea => Offset all the loops by the additive offset and get the area.
            //   Add the incremental volume, using trapezoidal approximation. 
            //   This should be accurate since the lines betweens data points are linear

            #region Same Setup as Area Decomposition
            outputData = new List<List<List<Point>>>();
            if (double.IsNaN(minOffset)) minOffset = Math.Sqrt(ts.SameTolerance);
            if (stepSize <= minOffset * 2)
            {
                //"step size must be at least 2x as large as the min offset");
                //Change it rather that throwing an exception
                stepSize = minOffset * 2 + ts.SameTolerance;
            }
            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Vertex> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { direction }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);
            #endregion

            var additiveVolume = 0.0;
            var previousDistance = 0.0;
            var previousArea = 0.0;
            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousDistanceAlongAxis = direction.dotProduct(sortedVertices[0].Position); //This value can be negative
            var maxDistance = direction.dotProduct(sortedVertices.Last().Position); //This value can be negative
            var previousVertexDistance = previousDistanceAlongAxis;
            var previousPolygons = new List<List<Point>>();
            foreach (var vertex in sortedVertices)
            {
                var distanceAlongAxis = direction.dotProduct(vertex.Position); //This value can be negative
                var difference1 = distanceAlongAxis - previousDistanceAlongAxis;
                var difference2 = distanceAlongAxis - previousVertexDistance;
                if (difference2 > minOffset && difference1 > stepSize)
                {
                    //Determine cross sectional area for section right after previous vertex
                    var distance = previousVertexDistance + minOffset; //X value (distance along axis) 
                    var cuttingPlane = new Flat(distance, direction);
                    List<List<Edge>> outputEdgeLoops;
                    var inputEdgeLoops = new List<List<Edge>>();
                    var current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops);

                    #region Get 2D polygons, offset, union, calculate area, calculate volume increment, and update variables
                    //Get a list of 2D paths from the 3D loops
                    var currentPaths = current3DLoops.Select(cp => MiscFunctions.Get2DProjectionPoints(cp, direction).ToList()).ToList();

                    //Offset if the additive accuracy is significant
                    var offsetPaths = !additiveAccuracy.IsNegligible() ? PolygonOperations.OffsetSquare(currentPaths, additiveAccuracy) : new List<List<Point>>(currentPaths);

                    var simpleOffset = offsetPaths.Select(PolygonOperations.SimplifyFuzzy).ToList();


                    //Union this new set of polygons with the previous set.
                    if (previousPolygons.Any()) //If not the first iteration
                    {
                        previousPolygons = previousPolygons.Select(PolygonOperations.SimplifyFuzzy).ToList();
                        try
                        {
                            currentPaths = new List<List<Point>>(PolygonOperations.Union(previousPolygons, simpleOffset));
                        }
                        catch
                        {
                            var testArea1 = simpleOffset.Sum(p => MiscFunctions.AreaOfPolygon(p));
                            var testArea2 = previousPolygons.Sum(p => MiscFunctions.AreaOfPolygon(p));
                            if (testArea1.IsPracticallySame(testArea2, 0.01))
                            {
                                currentPaths = simpleOffset;
                                    //They are probably throwing an error because they are closely overlapping
                            }
                            else
                            {
                                outputData.Clear();
                                outputData.Add(offsetPaths);
                                outputData.Add(simpleOffset);
                                outputData.Add(previousPolygons);
                                return 0;
                                throw new Exception("Union failed and not similar");
                            }
                        }
                    }

                    //Get the area of this layer
                    var area = currentPaths.Sum(p => MiscFunctions.AreaOfPolygon(p));

                    //Add the volume from this iteration.
                    if (!previousDistance.IsNegligible())
                    {
                        var deltaX = distance - previousDistance;
                        if (deltaX < 0 || area < previousArea*.99)
                        {
                            outputData.Clear();
                            outputData.Add(offsetPaths);
                            outputData.Add(simpleOffset);
                            outputData.Add(previousPolygons);
                            outputData.Add(currentPaths);
                            return 0;
                            throw new Exception("Error in your implementation. This should never occur");
                        }
                        additiveVolume +=  deltaX * previousArea;
                        outputData.Add(currentPaths);
                    }
        
                    previousPolygons = currentPaths;
                    previousDistance = distance;
                    previousArea = area;
                    #endregion

                    //If the difference is far enough, add another data point right before the current vertex
                    //Use the vertex loops provided from the first pass above
                    if (difference2 > 3 * minOffset)
                    {
                        var distance2 = distanceAlongAxis - minOffset; //X value (distance along axis) 
                        cuttingPlane = new Flat(distance2, direction);
                        current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops);

                        #region Get 2D polygons, offset, union, calculate area, calculate volume increment, and update variables
                        //Get a list of 2D paths from the 3D loops
                        currentPaths = current3DLoops.Select(cp => MiscFunctions.Get2DProjectionPoints(cp, direction).ToList()).ToList();

                        //Offset if the additive accuracy is significant
                        offsetPaths = !additiveAccuracy.IsNegligible() ? PolygonOperations.OffsetSquare(currentPaths, additiveAccuracy) : new List<List<Point>>(currentPaths);

                        simpleOffset = offsetPaths.Select(PolygonOperations.SimplifyFuzzy).ToList();

                        //Union this new set of polygons with the previous set.
                        if (previousPolygons.Any()) //If not the first iteration
                        {
                            previousPolygons = previousPolygons.Select(PolygonOperations.SimplifyFuzzy).ToList();
                            try
                            {
                                currentPaths = new List<List<Point>>(PolygonOperations.Union(previousPolygons, simpleOffset));
                            }
                            catch
                            {
                                var testArea1 = simpleOffset.Sum(p => MiscFunctions.AreaOfPolygon(p));
                                var testArea2 = previousPolygons.Sum(p => MiscFunctions.AreaOfPolygon(p));
                                if (testArea1.IsPracticallySame(testArea2, 0.01))
                                {
                                    currentPaths = simpleOffset;
                                        //They are probably throwing an error because they are closely overlapping
                                }
                                else
                                {
                                    outputData.Clear();
                                    outputData.Add(offsetPaths);
                                    outputData.Add(simpleOffset);
                                    outputData.Add(previousPolygons);
                                    return 0;
                                    throw new Exception("Union failed and not similar");
                                }
                            }
                        }

                        //Get the area of this layer
                        area = currentPaths.Sum(p => MiscFunctions.AreaOfPolygon(p));

                        //Add the volume from this iteration.
                        if (!previousDistance.IsNegligible())
                        {
                            var deltaX = distance - previousDistance;
                            if (deltaX < 0 || area < previousArea*.99)
                            {
                                outputData.Clear();
                                outputData.Add(offsetPaths);
                                outputData.Add(simpleOffset);
                                outputData.Add(previousPolygons);
                                outputData.Add(currentPaths);
                                return 0;
                                throw new Exception("Error in your implementation. This should never occur");
                            }
                            additiveVolume += deltaX * previousArea;
                            outputData.Add(currentPaths);
                        }
                        previousPolygons = currentPaths;
                        previousDistance = distance;
                        previousArea = area;
                        #endregion
                    }

                    //Update the previous distance used to make a data point
                    previousDistanceAlongAxis = distanceAlongAxis;
                }
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
                //Update the previous distance of the vertex checked
                previousVertexDistance = distanceAlongAxis;
            }
            //Now add the last data point.
            var finalDistance = maxDistance - previousDistance;
            additiveVolume += finalDistance * previousArea;
            return additiveVolume;
            //return outputData;
        }

        /// <summary>
        ///     Crosses the sectional area.
        /// </summary>
        /// <param name="edgeListDictionary">The edge list dictionary.</param>
        /// <param name="cuttingPlane">The cutting plane.</param>
        /// <param name="outputEdgeLoops">The output edge loops.</param>
        /// <param name="intputEdgeLoops">The intput edge loops.</param>
        /// <param name="ignoreNegativeSpace">if set to <c>true</c> [ignore negative space].</param>
        /// <returns>System.Double.</returns>
        /// <exception cref="Exception">Loop did not complete</exception>
        private static double CrossSectionalArea(Dictionary<int, Edge> edgeListDictionary, Flat cuttingPlane,
            out List<List<Edge>> outputEdgeLoops, List<List<Edge>> intputEdgeLoops, bool ignoreNegativeSpace = false)
        {
            var loops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, intputEdgeLoops);
            var totalArea = 0.0;
            foreach (var loop in loops)
            {
                //The area function returns negative values for negative loops and positive values for positive loops
                var area = MiscFunctions.AreaOf3DPolygon(loop, cuttingPlane.Normal);
                if (ignoreNegativeSpace && Math.Sign(area) < 0) continue;
                totalArea += area;
            }
            return totalArea;
        }

        private static IEnumerable<List<Vertex>> GetLoops(Dictionary<int, Edge> edgeListDictionary, Flat cuttingPlane,
            out List<List<Edge>> outputEdgeLoops, List<List<Edge>> intputEdgeLoops)
        {
            var edgeLoops = new List<List<Edge>>();
            var loops = new List<List<Vertex>>();
            if (intputEdgeLoops.Any())
            {
                edgeLoops = intputEdgeLoops; //Note that edge loops should all be ordered correctly
                loops.AddRange(edgeLoops.Select(edgeLoop => edgeLoop.Select(edge =>
                    MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal, cuttingPlane.DistanceToOrigin,
                        edge.To, edge.From)).ToList()));
            }
            else
            {
                //Build an edge list that we can modify, without ruining the original
                var edgeList = edgeListDictionary.ToDictionary(element => element.Key, element => element.Value);
                while (edgeList.Any())
                {
                    var startEdge = edgeList.ElementAt(0).Value;
                    var loop = new List<Vertex>();
                    var intersectVertex = MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                        cuttingPlane.DistanceToOrigin, startEdge.To, startEdge.From);
                    loop.Add(intersectVertex);
                    var edgeLoop = new List<Edge> { startEdge };
                    edgeList.Remove(startEdge.IndexInList);
                    var startFace = startEdge.OwnedFace;
                    var currentFace = startFace;
                    var previousFace = startFace; //This will be set again before its used.
                    var endFace = startEdge.OtherFace;
                    var nextEdgeFound = false;
                    Edge nextEdge = null;
                    var correctDirection = 0.0;
                    var reverseDirection = 0.0;
                    do
                    {
                        foreach (var edge in edgeList.Values)
                        {
                            if (edge.OtherFace == currentFace)
                            {
                                previousFace = edge.OtherFace;
                                currentFace = edge.OwnedFace;
                                nextEdgeFound = true;
                                nextEdge = edge;
                                break;
                            }
                            if (edge.OwnedFace == currentFace)
                            {
                                previousFace = edge.OwnedFace;
                                currentFace = edge.OtherFace;
                                nextEdgeFound = true;
                                nextEdge = edge;
                                break;
                            }
                        }
                        if (nextEdgeFound)
                        {
                            //For the first set of edges, check to make sure this list is going in the proper direction
                            intersectVertex = MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                                cuttingPlane.DistanceToOrigin, nextEdge.To, nextEdge.From);
                            var vector = intersectVertex.Position.subtract(loop.Last().Position);
                            //Use the previous face, since that is the one that contains both of the edges that are in use.
                            var dot = cuttingPlane.Normal.crossProduct(previousFace.Normal).dotProduct(vector);
                            loop.Add(intersectVertex);
                            edgeLoop.Add(nextEdge);
                            edgeList.Remove(nextEdge.IndexInList);
                            //Note that removing at an index is FASTER than removing a object.
                            if (Math.Sign(dot) >= 0) correctDirection++;
                            else reverseDirection++;
                        }
                        else throw new Exception("Loop did not complete");
                    } while (currentFace != endFace);
                    if (reverseDirection > correctDirection)
                    {
                        loop.Reverse();
                        edgeLoop.Reverse();
                    }
                    loops.Add(loop);
                    edgeLoops.Add(edgeLoop);
                }
            }
            outputEdgeLoops = edgeLoops;
            return loops;
        }

        /// <summary>
        ///     Convexes the hull2 d area.
        /// </summary>
        /// <param name="edgeList">The edge list.</param>
        /// <param name="cuttingPlane">The cutting plane.</param>
        /// <returns>System.Double.</returns>
        private static double ConvexHull2DArea(Dictionary<int, Edge> edgeList, Flat cuttingPlane)
        {
            //Don't bother with loops. Just get all the intercept vertices, project to 2d and run 2dConvexHull
            var vertices =
                edgeList.Select(
                    edge =>
                        MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                            cuttingPlane.DistanceToOrigin, edge.Value.To, edge.Value.From)).ToList();
            var points = MiscFunctions.Get2DProjectionPoints(vertices.ToArray(), cuttingPlane.Normal, true);
            return MinimumEnclosure.ConvexHull2DArea(MinimumEnclosure.ConvexHull2D(points));
        }

        /// <summary>
        ///     Boundings the rectangle area.
        /// </summary>
        /// <param name="edgeList">The edge list.</param>
        /// <param name="cuttingPlane">The cutting plane.</param>
        /// <returns>System.Double.</returns>
        private static double BoundingRectangleArea(Dictionary<int, Edge> edgeList, Flat cuttingPlane)
        {
            //Don't bother with loops. Just get all the intercept vertices, project to 2d and run 2dConvexHull
            var vertices =
                edgeList.Select(
                    edge =>
                        MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                            cuttingPlane.DistanceToOrigin, edge.Value.To, edge.Value.From)).ToList();
            var points = MiscFunctions.Get2DProjectionPoints(vertices.ToArray(), cuttingPlane.Normal, true);
            var boundingRectangle = MinimumEnclosure.BoundingRectangle(points, false);
            return boundingRectangle.Area;
        }
    }
}