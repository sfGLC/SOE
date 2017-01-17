using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Collections.Specialized;
using System.Runtime.InteropServices;

using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Server;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.SOESupport;


//TODO: sign the project (project properties > signing tab > sign the assembly)
//      this is strongly suggested if the dll will be registered using regasm.exe <your>.dll /codebase


namespace RestSOE1
{
    [ComVisible(true)]
    [Guid("615ad9f4-9ca0-4bca-ae49-de78181c0d97")]
    [ClassInterface(ClassInterfaceType.None)]
    [ServerObjectExtension("MapServer",//use "MapServer" if SOE extends a Map service and "ImageServer" if it extends an Image service.
        AllCapabilities = "",
        DefaultCapabilities = "",
        Description = "Spatial Query REST SOE Sample",
        DisplayName = "RestSOE1",
        Properties = "FiledName=PRIMARY_;LayerName=veg",
        SupportsREST = true,
        SupportsSOAP = false)]
    public class RestSOE1 : IServerObjectExtension, IObjectConstruct, IRESTRequestHandler
    {
        private string soe_name;

        private IPropertySet configProps;
        private IServerObjectHelper serverObjectHelper;
        private ServerLogger logger;
        private IRESTRequestHandler reqHandler;

        private IFeatureClass m_fcToQuery;
        private string m_mapLayerNameToQuery;
        private string m_mapFieldToQuery;


        public RestSOE1()
        {
            soe_name = this.GetType().Name;
            logger = new ServerLogger();
            reqHandler = new SoeRestImpl(soe_name, CreateRestSchema()) as IRESTRequestHandler;
        }

        #region IServerObjectExtension Members

        public void Init(IServerObjectHelper pSOH)
        {
            serverObjectHelper = pSOH;
        }

        public void Shutdown()
        {
            logger.LogMessage(ServerLogger.msgType.infoStandard, "Shutdown", 8000, "Custom message: Shutting down the SOE");
            soe_name = null;
            m_fcToQuery = null;
            m_mapFieldToQuery = null;
            serverObjectHelper = null;
            logger = null;
        }

        #endregion

        #region IObjectConstruct Members

        public void Construct(IPropertySet props)
        {
            configProps = props;

            if (props.GetProperty("FieldName") != null)
            {
                m_mapFieldToQuery = props.GetProperty("FieldName") as string;
            }
            else
            {
                throw new ArgumentNullException();
            }
            if (props.GetProperty("LayerName") != null)
            {
                m_mapLayerNameToQuery = props.GetProperty("LayerName") as string;
            }
            else
            {
                throw new ArgumentNullException();
            }
            try
            {
                IMapServer3 mapServer = (IMapServer3)serverObjectHelper.ServerObject;
                string mapName = mapServer.DefaultMapName;
                IMapLayerInfo layerInfo;
                IMapLayerInfos layerInfos = mapServer.GetServerInfo(mapName).MapLayerInfos;

                int c = layerInfos.Count;
                int layerIndex = 0;
                for (int i = 0; i < c; i++)
                {
                    layerInfo = layerInfos.get_Element(i);
                    if (layerInfo.Name == m_mapLayerNameToQuery)
                    {
                        layerIndex = i;
                        break;
                    }
                }

                IMapServerDataAccess dataAccess = (IMapServerDataAccess)mapServer;

                m_fcToQuery = (IFeatureClass)dataAccess.GetDataSource(mapName, layerIndex);
                if (m_fcToQuery == null)
                {
                    logger.LogMessage(ServerLogger.msgType.error, "Construct", 8000, "SOE custom error: Layer name not found.");
                    return;
                }
                if (m_fcToQuery.FindField(m_mapFieldToQuery) == -1)
                {
                    logger.LogMessage(ServerLogger.msgType.error, "Construct", 8000, "SOE custom error: Field not found in layer.");
                }
                
            }
            catch
            {
                logger.LogMessage(ServerLogger.msgType.error, "Construct", 8000, "SOE custom error: Could not get the feature layer.");
            }


        }

        #endregion

        #region IRESTRequestHandler Members

        public string GetSchema()
        {
            return reqHandler.GetSchema();
        }

        public byte[] HandleRESTRequest(string Capabilities, string resourceName, string operationName, string operationInput, string outputFormat, string requestProperties, out string responseProperties)
        {
            return reqHandler.HandleRESTRequest(Capabilities, resourceName, operationName, operationInput, outputFormat, requestProperties, out responseProperties);
        }

        #endregion

        private RestResource CreateRestSchema()
        {
            RestResource rootRes = new RestResource(soe_name, false, RootResHandler);

            RestOperation spatialQueryOper = new RestOperation("SpatialQuery",
                                                      new string[] { "location", "distance" },
                                                      new string[] { "json" },
                                                      SampleOperHandler);

            rootRes.operations.Add(spatialQueryOper);

            return rootRes;
        }

        private byte[] RootResHandler(NameValueCollection boundVariables, string outputFormat, string requestProperties, out string responseProperties)
        {
            responseProperties = null;

            JsonObject result = new JsonObject();
            //result.AddString("hello", "world");

            return Encoding.UTF8.GetBytes(result.ToJson());
        }

        private byte[] SampleOperHandler(NameValueCollection boundVariables,
                                                  JsonObject operationInput,
                                                      string outputFormat,
                                                      string requestProperties,
                                                  out string responseProperties)
        {
            responseProperties = null;

            string parm1Value;
            bool found = operationInput.TryGetString("parm1", out parm1Value);
            if (!found || string.IsNullOrEmpty(parm1Value))
                throw new ArgumentNullException("parm1");

            string parm2Value;
            found = operationInput.TryGetString("parm2", out parm2Value);
            if (!found || string.IsNullOrEmpty(parm2Value))
                throw new ArgumentNullException("parm2");

            JsonObject result = new JsonObject();
            result.AddString("parm1", parm1Value);
            result.AddString("parm2", parm2Value);

            return Encoding.UTF8.GetBytes(result.ToJson());
        }

        private byte[] SpatialQueryOperationHandler(NameValueCollection boundVariables,
                                                    JsonObject operationInput,
                                                    string outputFormat,
                                                    string requestProperties,
                                                    out string responseProperties)
        {
            responseProperties = null;
            JsonObject jsonPoint;
            if (!operationInput.TryGetJsonObject("location", out jsonPoint))
            {
                throw new ArgumentNullException("location");
            }
            IPoint location = Conversion.ToGeometry(jsonPoint, esriGeometryType.esriGeometryPoint) as IPoint;
            if (location == null)
            {
                throw new ArgumentNullException("SpatialQueryREST: invalid location", "location");
            }

            double ? distance;
            if (!operationInput.TryGetAsDouble("distance", out distance)||!distance.HasValue)
            {
                throw new ArgumentNullException("SpatialQueryREST: invalid distance", "distance");
            }
            byte[] result = QueryPoint(location, distance.Value);
            return result;
        }

        private byte[] QueryPoint(ESRI.ArcGIS.Geometry.IPoint location, double distance)
        {
            if (distance <= 0)
            {
                throw new ArgumentOutOfRangeException("distance");
            }

            ITopologicalOperator topologicalOperator = (ESRI.ArcGIS.Geometry.ITopologicalOperator) location;
            IGeometry queryGeometry = topologicalOperator.Buffer(distance);
            ISpatialFilter spatialFilter = new ESRI.ArcGIS.Geodatabase.SpatialFilter();
            spatialFilter.Geometry = queryGeometry;
            spatialFilter.SpatialRel = ESRI.ArcGIS.Geodatabase.esriSpatialRelEnum.esriSpatialRelIntersects;
            spatialFilter.GeometryField = m_fcToQuery.ShapeFieldName;
            IFeatureCursor resultsFeatureCursor = m_fcToQuery.Search(spatialFilter, true);

            topologicalOperator = (ESRI.ArcGIS.Geometry.ITopologicalOperator)queryGeometry;
            int classFieldIndex = m_fcToQuery.FindField(m_mapFieldToQuery);

            Dictionary<string, double> summaryStatsDictionary = new Dictionary<string, double>();
            List<JsonObject> jsonGeometreis = new List<JsonObject>();

            IFeature resultsFeature = null;
            while ((resultsFeature = resultsFeatureCursor.NextFeature()) != null)
            {
                IPolygon clippedResultsGeometry = (IPolygon)topologicalOperator.Intersect(resultsFeature.Shape,
                                                                                          ESRI.ArcGIS.Geometry.esriGeometryDimension.esriGeometry2Dimension);
                clippedResultsGeometry.Densify(0, 0);
                JsonObject jsonClippedResultsGeometry = Conversion.ToJsonObject(clippedResultsGeometry);
                jsonGeometreis.Add(jsonClippedResultsGeometry);

                IArea area = (IArea)clippedResultsGeometry;
                string resultsClass = resultsFeature.get_Value(classFieldIndex) as string;

                if (summaryStatsDictionary.ContainsKey(resultsClass))
                {
                    summaryStatsDictionary[resultsClass] = (double)summaryStatsDictionary[resultsClass] + area.Area;
                }
                else
                {
                    summaryStatsDictionary[resultsClass] = area.Area;
                }
            }

            JsonObject[] areaResultJson = CreateJsonRecords(summaryStatsDictionary) as JsonObject[];
            JsonObject resultJsonObject = new JsonObject();
            resultJsonObject.AddArray("geometries", jsonGeometreis.ToArray());
            resultJsonObject.AddArray("records", areaResultJson);

            byte[] result = Encoding.UTF8.GetBytes(resultJsonObject.ToJson());
            return result;
        }

        private JsonObject[] CreateJsonRecords(Dictionary<string, double> inListDictionary)
        {
            JsonObject[] jsonRecordsArray = new JsonObject[inListDictionary.Count];
            int i = 0;
            foreach (KeyValuePair<string, double> kvp in inListDictionary)
            {
                string currentKey = kvp.Key.ToString();
                string currentValue = kvp.Value.ToString();
                JsonObject currentKeyValue = new JsonObject();
                currentKeyValue.AddString(m_mapLayerNameToQuery, currentKey);
                currentKeyValue.AddString("value", currentValue);
                jsonRecordsArray.SetValue(currentKeyValue, i);
                i++;
            }
            return jsonRecordsArray;
        }
    }
}
