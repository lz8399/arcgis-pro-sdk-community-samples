﻿//   Copyright 2017 Esri

//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at

//       http://www.apache.org/licenses/LICENSE-2.0

//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Editing;
using ArcGIS.Core.Data;
using ArcGIS.Core.CIM;

namespace AnnoTools
{
  /// <summary>
  /// Illustrates how to add a line callout with a leader line to an annotation feature. 
  /// </summary>
  /// <remarks>
  /// Use the GetGraphic and SetGraphic methods on the AnnotationFeature object to get and set the CIMTextSymbol of an annotation feature. 
  /// Use these methods in conjunction with the EditOperation.Callback method to wrap the edit in an edit operation. You must also ensure
  /// that you're using a non-recycling cursor when updating the annotation feature (via Table.Search). 
  /// <para></para>
  /// There is no other way (in ArcGIS Pro 2.1) to modify the CIMTextSymbol.
  /// <para></para>
  /// </remarks>
  internal class SimpleLineCallout : MapTool
  {
    public SimpleLineCallout()
    {
      IsSketchTool = true;
      SketchType = SketchGeometryType.Rectangle;
      SketchOutputMode = SketchOutputMode.Map;
    }

    protected override Task OnToolActivateAsync(bool active)
    {
      return base.OnToolActivateAsync(active);
    }

    protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
    {
      // execute on the MCT
      return QueuedTask.Run(() =>
      {
        // find features under the sketch 
        var features = MapView.Active.GetFeatures(geometry);
        if (features.Count == 0)
          return false;

        EditOperation op = null;
        foreach (var layerKey in features.Keys)
        {
          // is it an anno layer?
          if (!(layerKey is AnnotationLayer))
            continue;

          // are there features?
          var featOids = features[layerKey];
          if (featOids.Count == 0)
            continue;

          // for each feature
          foreach (var oid in featOids)
          {
            // create the edit operation
            if (op == null)
            {
              op = new EditOperation();
              op.Name = "Alter symbol to simple line callout";
              op.SelectModifiedFeatures = true;
              op.SelectNewFeatures = false;
            }

            // use the callback method
            op.Callback(context =>
            {
              // find the feature
              QueryFilter qf = new QueryFilter();
              qf.WhereClause = "OBJECTID = " + oid.ToString();

              // use the table
              using (var table = layerKey.GetTable())
              {
                // make sure you use a non-recycling cursor
                using (var rowCursor = table.Search(qf, false))
                {
                  rowCursor.MoveNext();
                  if (rowCursor.Current != null)
                  {
                    ArcGIS.Core.Data.Mapping.AnnotationFeature annoFeature = rowCursor.Current as ArcGIS.Core.Data.Mapping.AnnotationFeature;
                    if (annoFeature != null)
                    {
                      // get the CIMTextGraphic
                      var textGraphic = annoFeature.GetGraphic() as CIMTextGraphic;

                      if (textGraphic != null)
                      {
                        //
                        // add a leader point to the text graphic
                        //

                        // get the feature shape
                        var feature = annoFeature as Feature;
                        var textExtent = feature.GetShape();

                        // find the lower left of the text extent
                        var extent = textExtent.Extent;
                        var lowerLeft = MapPointBuilder.CreateMapPoint(extent.XMin, extent.YMin, textExtent.SpatialReference);
                        // move it a little to the left and down
                        var newPoint = GeometryEngine.Instance.Move(lowerLeft, -40, -40);

                        // create a leader point
                        CIMLeaderPoint leaderPt = new CIMLeaderPoint();
                        leaderPt.Point = newPoint as MapPoint;

                        // add to a list
                        List<CIMLeader> leaderArray = new List<CIMLeader>();
                        leaderArray.Add(leaderPt);

                        // assign to the textGraphic
                        textGraphic.Leaders = leaderArray.ToArray();

                        //
                        // add the simpleLineCallout
                        //

                        CIMSimpleLineCallout lineCallout = new CIMSimpleLineCallout();
                        lineCallout.LineSymbol = SymbolFactory.Instance.ConstructLineSymbol(ColorFactory.Instance.GreyRGB, 1, SimpleLineStyle.Dash);


                        // asign it to the textSymbol
                        var symbol = textGraphic.Symbol.Symbol;
                        var textSymbol = symbol as CIMTextSymbol;

                        textSymbol.Callout = lineCallout;

                        try
                        {
                          // update the graphic
                          annoFeature.SetGraphic(textGraphic);
                          // store
                          annoFeature.Store();

                          // refresh the cache
                          context.Invalidate(annoFeature);
                        }

                        // SetGraphic can throw a GeodatabaseException if the AnnotationFeatureClassDefinition AreSymbolOverridesAllowed = false
                        //  or if IsSymbolIDRequired = true and the symbol edit you're making causes the symbol to be disconnected from the symbol collection.
                        //   see http://pro.arcgis.com/en/pro-app/sdk/api-reference/#topic17424.html
                        //   and http://pro.arcgis.com/en/pro-app/sdk/api-reference/#topic17432.html
                        catch (GeodatabaseException ex)
                        {
                        }
                      }
                    }

                  }
                }
              }
            }, layerKey.GetTable());
          }
        }

        if ((op != null) && !op.IsEmpty)
        {
          bool result = op.Execute();
          return result;
        }
        return
          false;
      });

    }

  }
}
