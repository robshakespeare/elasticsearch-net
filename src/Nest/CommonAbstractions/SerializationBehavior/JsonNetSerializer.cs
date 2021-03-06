using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elasticsearch.Net;
using Newtonsoft.Json;

namespace Nest
{
	public class JsonNetSerializer : IElasticsearchSerializer
	{
		private static readonly Encoding ExpectedEncoding = new UTF8Encoding(false);

		private readonly IConnectionSettingsValues _settings;
		private readonly Dictionary<SerializationFormatting, JsonSerializer> _defaultSerializers;
		private readonly JsonSerializer _defaultSerializer;
		internal JsonSerializer Serializer => _defaultSerializer;
		private ElasticContractResolver _contractResolver;

		protected virtual void ModifyJsonSerializerSettings(JsonSerializerSettings settings) { }
		protected virtual IList<Func<Type, JsonConverter>> ContractConverters => null;

		public JsonNetSerializer(IConnectionSettingsValues settings) : this(settings, null) { }

		/// <summary>
		/// this constructor is only here for stateful (de)serialization 
		/// </summary>
		internal JsonNetSerializer(IConnectionSettingsValues settings, JsonConverter stateFullConverter)
		{
			this._settings = settings;
			var piggyBackState = stateFullConverter == null ? null : new JsonConverterPiggyBackState { ActualJsonConverter = stateFullConverter };
			// ReSharper disable once VirtualMemberCallInContructor
			this._contractResolver = new ElasticContractResolver(this._settings, this.ContractConverters) { PiggyBackState = piggyBackState };

			this._defaultSerializer = JsonSerializer.Create(this.CreateSettings(SerializationFormatting.None));
			//this._defaultSerializer.Formatting = Formatting.None; 
			var indentedSerializer = JsonSerializer.Create(this.CreateSettings(SerializationFormatting.Indented));
			//indentedSerializer.Formatting = Formatting.Indented; 
			this._defaultSerializers = new Dictionary<SerializationFormatting, JsonSerializer>
			{
				{ SerializationFormatting.None, this._defaultSerializer },
				{ SerializationFormatting.Indented, indentedSerializer }
			};
		}

		public virtual void Serialize(object data, Stream writableStream, SerializationFormatting formatting = SerializationFormatting.Indented)
		{
			var serializer = _defaultSerializers[formatting];
			using (var writer = new StreamWriter(writableStream, ExpectedEncoding, 8096, leaveOpen: true))
			using (var jsonWriter = new JsonTextWriter(writer))
			{
				serializer.Serialize(jsonWriter, data);
				writer.Flush();
				jsonWriter.Flush();
			}
		}

		public virtual string CreatePropertyName(MemberInfo memberInfo)
		{
			var jsonProperty = memberInfo.GetCustomAttribute<JsonPropertyAttribute>(true);
			return jsonProperty?.PropertyName;
		}

		public virtual T Deserialize<T>(Stream stream)
		{
			if (stream == null) return default(T);
			using (var streamReader = new StreamReader(stream))
			using (var jsonTextReader = new JsonTextReader(streamReader))
			{
				var t = this._defaultSerializer.Deserialize(jsonTextReader, typeof(T));
				return (T)t;
			}
		}

		public virtual Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
		{
			//Json.NET does not support reading a stream asynchronously :(
			var result = this.Deserialize<T>(stream);
			return Task.FromResult<T>(result);
		}

		private JsonSerializerSettings CreateSettings(SerializationFormatting formatting)
		{
			var settings = new JsonSerializerSettings()
			{
				Formatting = formatting == SerializationFormatting.Indented ? Formatting.Indented : Formatting.None,
				ContractResolver = this._contractResolver,
				DefaultValueHandling = DefaultValueHandling.Include,
				NullValueHandling = NullValueHandling.Ignore
			};

			this.ModifyJsonSerializerSettings(settings);

			var contract = settings.ContractResolver as ElasticContractResolver;
			if (contract == null) throw new Exception($"NEST needs an instance of {nameof(ElasticContractResolver)} registered on Json.NET's JsonSerializerSettings");

			return settings;
		}
	}
}