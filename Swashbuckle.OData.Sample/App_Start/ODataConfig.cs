﻿using System.Collections.Generic;
using System.Net.Http;
using System.Web.Configuration;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using Microsoft.AspNet.OData.Builder;
using Microsoft.AspNet.OData.Extensions;
using Microsoft.AspNet.OData.Routing;
using Microsoft.AspNet.OData.Routing.Conventions;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.Restier.Providers.EntityFramework;
using Microsoft.Restier.Publishers.OData;
using Microsoft.Restier.Publishers.OData.Batch;
using Swashbuckle.OData;
using SwashbuckleODataSample.Models;
using SwashbuckleODataSample.Repositories;
using SwashbuckleODataSample.Versioning;

namespace SwashbuckleODataSample
{
    public static class ODataConfig
    {
        public const string ODataRoutePrefix = "odata";

        public static void Register(HttpConfiguration config)
        {
            ConfigureWebApiOData(config);
            ConfigureRestierOData(config);
        }

        private static async void ConfigureRestierOData(HttpConfiguration config)
        {
            config.Filter().Expand().Select().OrderBy().MaxTop(null).Count();
            await config.MapRestierRoute<EntityFrameworkApi<RestierODataContext>>("RESTierRoute", 
                      "restier", 
                      new RestierBatchHandler(GlobalConfiguration.DefaultServer));
        }

        private static void ConfigureWebApiOData(HttpConfiguration config)
        {
            var controllerSelector = new ODataVersionControllerSelector(config);
            config.Services.Replace(typeof(IHttpControllerSelector), controllerSelector);

            // Define a versioned route
            config.MapODataServiceRoute("V1RouteVersioning", "odata/v1", GetVersionedModel());
            controllerSelector.RouteVersionSuffixMapping.Add("V1RouteVersioning", "V1");

            // Define a versioned route that doesn't map to any controller
            config.MapODataServiceRoute("odata/v2", "odata/v2", GetFakeModel());
            controllerSelector.RouteVersionSuffixMapping.Add("odata/v2", "V2");

            // Define a custom route with custom routing conventions
            var conventions = ODataRoutingConventions.CreateDefault();
            conventions.Insert(0, new CustomNavigationPropertyRoutingConvention());
            var customODataRoute = config.MapODataServiceRoute("CustomODataRoute", ODataRoutePrefix, GetCustomRouteModel(), batchHandler: null, pathHandler: new DefaultODataPathHandler(), routingConventions: conventions);
            config.AddCustomSwaggerRoute(customODataRoute, "/Customers({Id})/Orders")
                .Operation(HttpMethod.Post)
                .PathParameter<int>("Id")
                .BodyParameter<Order>("order");

            // Define a route to a controller class that contains functions
            config.MapODataServiceRoute("FunctionsODataRoute", ODataRoutePrefix, GetFunctionsEdmModel());

            // Define a default non- versioned route(default route should be at the end as a last catch-all)
            config.MapODataServiceRoute("DefaultODataRoute", ODataRoutePrefix, GetDefaultModel());

            bool isPrefixFreeEnabled = System.Convert.ToBoolean(WebConfigurationManager.AppSettings["EnableEnumPrefixFree"]);
            var uriResolver = isPrefixFreeEnabled ? new StringAsEnumResolver() : new ODataUriResolver();

            // Define a route with an enum as a key
            const string enumRouteName = "EnumODataRoute";
            config.MapODataServiceRoute(enumRouteName,
                                        ODataRoutePrefix,
                                        builder => builder
                                            .AddService(ServiceLifetime.Singleton, sp => GetProductWithEnumKeyModel())
                                            .AddService(ServiceLifetime.Singleton, sp => (IEnumerable<IODataRoutingConvention>)ODataRoutingConventions.CreateDefaultWithAttributeRouting(enumRouteName, config))
                                            .AddService(ServiceLifetime.Singleton, sp => uriResolver));

            // Define a route with an enum/int composite key
            const string enumIntCompositeRouteName = "EnumIntCompositeODataRoute";
            config.MapODataServiceRoute(enumIntCompositeRouteName,
                                        ODataRoutePrefix,
                                        builder => builder
                                            .AddService(ServiceLifetime.Singleton, sp => GetProductWithCompositeEnumIntKeyModel())
                                            .AddService(ServiceLifetime.Singleton, sp => (IEnumerable<IODataRoutingConvention>)ODataRoutingConventions.CreateDefaultWithAttributeRouting(enumIntCompositeRouteName, config))
                                            .AddService(ServiceLifetime.Singleton, sp => uriResolver));
        }

        private static IEdmModel GetDefaultModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<Customer>("Customers");
            builder.EntitySet<Order>("Orders");

            return builder.GetEdmModel();
        }

        private static IEdmModel GetCustomRouteModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<Customer>("Customers");
            builder.EntitySet<Order>("Orders");

            return builder.GetEdmModel();
        }

        private static IEdmModel GetVersionedModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<Customer>("Customers");

            return builder.GetEdmModel();
        }

        private static IEdmModel GetFakeModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<Customer>("FakeCustomers");

            return builder.GetEdmModel();
        }

        private static IEdmModel GetFunctionsEdmModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<Product>("Products");

            var productType = builder.EntityType<Product>();

            // Function bound to a collection that accepts an enum parameter
            var enumParamFunction = productType.Collection.Function("GetByEnumValue");
            enumParamFunction.Parameter<MyEnum>("EnumValue");
            enumParamFunction.ReturnsCollectionFromEntitySet<Product>("Products");

            var enumParamEntityFunction = productType.Function("IsEnumValueMatch");
            enumParamEntityFunction.Parameter<MyEnum>("EnumValue");
            enumParamEntityFunction.Returns<bool>();

            // Function bound to an entity set
            // Returns the most expensive product, a single entity
            productType.Collection
                .Function("MostExpensive")
                .Returns<double>();

            // Function bound to an entity set
            // Returns the top 10 product, a collection
            productType.Collection
                .Function("Top10")
                .ReturnsCollectionFromEntitySet<Product>("Products");

            // Function bound to a single entity
            // Returns the instance's price rank among all products
            productType
                .Function("GetPriceRank")
                .Returns<int>();

            // Function bound to a single entity
            // Accept a string as parameter and return a double
            // This function calculate the general sales tax base on the 
            // state
            productType
                .Function("CalculateGeneralSalesTax")
                .Returns<double>()
                .Parameter<string>("state");

            // Function bound to an entity set
            // Accepts an array as a parameter
            productType.Collection
                .Function("ProductsWithIds")
                .ReturnsCollectionFromEntitySet<Product>("Products")
                .CollectionParameter<int>("Ids");

            // An action bound to an entity set
            // Accepts multiple action parameters
            var createAction = productType.Collection.Action("Create");
                createAction.ReturnsFromEntitySet<Product>("Products");
                createAction.Parameter<string>("Name").OptionalParameter = false;
                createAction.Parameter<double>("Price").OptionalParameter = false;
                createAction.Parameter<MyEnum>("EnumValue").OptionalParameter = false;

            // An action bound to an entity set
            // Accepts an array of complex types
            var postArrayAction = productType.Collection.Action("PostArray");
            postArrayAction.ReturnsFromEntitySet<Product>("Products");
            postArrayAction.CollectionParameter<ProductDto>("products").OptionalParameter = false;

            // An action bound to an entity
            productType.Action("Rate")
                .Parameter<int>("Rating");

            return builder.GetEdmModel();
        }

        #region Enum Routes
        private static IEdmModel GetProductWithEnumKeyModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<ProductWithEnumKey>("ProductWithEnumKeys");

            return builder.GetEdmModel();
        }

        private static IEdmModel GetProductWithCompositeEnumIntKeyModel()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EnableLowerCamelCase();

            builder.EntitySet<ProductWithCompositeEnumIntKey>
                            ("ProductWithCompositeEnumIntKeys");

            return builder.GetEdmModel();
        }
        #endregion
    }
}