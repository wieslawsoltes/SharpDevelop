/*
 * Created by SharpDevelop.
 * User: Peter Forstmeier
 * Date: 24.02.2014
 * Time: 20:02
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections;
using System.ComponentModel.Design.Serialization;
using System.IO;
using System.Text;
using System.Xml;
using ICSharpCode.Core;
using ICSharpCode.Reporting.Factories;
using ICSharpCode.Reporting.Items;
using ICSharpCode.Reporting.Addin.Services;

namespace ICSharpCode.Reporting.Addin.DesignerBinding
{
	/// <summary>
	/// Description of ReportDesignerLoader.
	/// </summary>
	public class ReportDesignerLoader: BasicDesignerLoader
	{
		IDesignerLoaderHost host;
		readonly IDesignerGenerator generator;
		byte[] reportContent;
		byte[] pendingReloadContent;
		bool loadingPendingReload;
		bool recoveringFailedReload;

		public event EventHandler ReloadFailed;

		public bool RecoveringFailedReload {
			get { return recoveringFailedReload; }
		}
		
		#region Constructors

		public ReportDesignerLoader(IDesignerGenerator generator, Stream stream){
			if (stream == null)
				throw new ArgumentNullException("stream");
			if (generator == null) {
				throw new ArgumentNullException("generator");
			}
			this.generator = generator;
			reportContent = ReadReportContent(stream);
		}
		
		#endregion

		
		#region Overriden methods of BasicDesignerLoader
		
		protected override void Initialize(){
			LoggingService.Info("ReportDesignerLoader:Initialize");
			base.Initialize();
			host = LoaderHost;
			host.AddService(typeof(ComponentSerializationService), new CodeDomComponentSerializationService((IServiceProvider)host));
			host.AddService(typeof(IDesignerSerializationService), new DesignerSerializationService((IServiceProvider)host));
		}
		
		
		protected override void PerformLoad(IDesignerSerializationManager serializationManager){
			LoggingService.Info("ReportDesignerLoader:PerformLoad"); 
			loadingPendingReload = pendingReloadContent != null;
			byte[] content = pendingReloadContent ?? reportContent;
			using (var stream = new MemoryStream(content, false)) {
				var internalLoader = new InternalReportLoader(host,generator, stream);
				internalLoader.LoadOrCreateReport();
			}
		}


		protected override void OnEndLoad(bool successful, ICollection errors)
		{
			bool completedPendingReload = loadingPendingReload;
			bool pendingReloadSucceeded = successful && (errors == null || errors.Count == 0);

			if (completedPendingReload) {
				if (pendingReloadSucceeded) {
					reportContent = pendingReloadContent;
					recoveringFailedReload = false;
				} else {
					recoveringFailedReload = true;
				}
				pendingReloadContent = null;
			} else if (successful && recoveringFailedReload) {
				recoveringFailedReload = false;
			}
			loadingPendingReload = false;

			base.OnEndLoad(successful, errors);

			if (completedPendingReload && !pendingReloadSucceeded) {
				ReloadFailed?.Invoke(this, EventArgs.Empty);
				Reload(ReloadOptions.Force | ReloadOptions.NoFlush);
			}
		}
 
		
		protected override void PerformFlush(IDesignerSerializationManager designerSerializationManager){
			LoggingService.Info("ReportDesignerLoader:PerformFlush");
			generator.MergeFormChanges((System.CodeDom.CodeCompileUnit)null);
			SetReportContent(generator.ReportFileContent);
		}

		public bool ReloadFrom(Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");
			if (Loading || ReloadPending)
				return false;

			pendingReloadContent = ReadReportContent(stream);
			try {
				Reload(ReloadOptions.Force | ReloadOptions.NoFlush);
			} catch {
				pendingReloadContent = null;
				throw;
			}

			if (!ReloadPending) {
				pendingReloadContent = null;
				return false;
			}

			return true;
		}

		public void WriteReportContent(Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");

			stream.Write(reportContent, 0, reportContent.Length);
		}
		
		#endregion

		
		#region Serialize to Xml
		
		public XmlDocument SerializeModel()
		{
			Flush();
			var doc = new XmlDocument();
			using (var stream = new MemoryStream(reportContent, false)) {
				doc.Load(stream);
			}
			return doc;
		}

		static byte[] ReadReportContent(Stream stream)
		{
			long position = stream.CanSeek ? stream.Position : 0;
			try {
				using (var copy = new MemoryStream()) {
					stream.CopyTo(copy);
					return copy.ToArray();
				}
			} finally {
				if (stream.CanSeek) {
					stream.Position = position;
				}
			}
		}

		void SetReportContent(string content)
		{
			if (String.IsNullOrEmpty(content)) {
				throw new InvalidOperationException("The Reporting generator produced no report content.");
			}

			reportContent = Encoding.UTF8.GetBytes(content);
		}
		
		#endregion
	}
}
