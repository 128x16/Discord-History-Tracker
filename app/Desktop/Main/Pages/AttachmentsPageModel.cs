using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CommunityToolkit.Mvvm.ComponentModel;
using DHT.Desktop.Common;
using DHT.Desktop.Dialogs.Message;
using DHT.Desktop.Main.Controls;
using DHT.Server;
using DHT.Server.Data;
using DHT.Server.Data.Aggregations;
using DHT.Server.Data.Filters;
using DHT.Utils.Logging;
using DHT.Utils.Tasks;

namespace DHT.Desktop.Main.Pages;

sealed partial class AttachmentsPageModel : ObservableObject, IDisposable {
	private static readonly Log Log = Log.ForType<AttachmentsPageModel>();

	private static readonly DownloadItemFilter EnqueuedItemFilter = new () {
		IncludeStatuses = new HashSet<DownloadStatus> {
			DownloadStatus.Enqueued,
			DownloadStatus.Downloading
		}
	};

	[ObservableProperty(Setter = Access.Private)]
	private bool isToggleDownloadButtonEnabled = true;

	public string ToggleDownloadButtonText => IsDownloading ? "Stop Downloading" : "Start Downloading";

	[ObservableProperty(Setter = Access.Private)]
	[NotifyPropertyChangedFor(nameof(IsRetryFailedOnDownloadsButtonEnabled))]
	private bool isRetryingFailedDownloads = false;

	[ObservableProperty(Setter = Access.Private)]
	[NotifyPropertyChangedFor(nameof(IsRetryFailedOnDownloadsButtonEnabled))]
	private bool hasFailedDownloads;

	public bool IsRetryFailedOnDownloadsButtonEnabled => !IsRetryingFailedDownloads && HasFailedDownloads;

	[ObservableProperty(Setter = Access.Private)]
	private string downloadMessage = "";

	public double DownloadProgress => totalItemsToDownloadCount is null or 0 ? 0.0 : 100.0 * doneItemsCount / totalItemsToDownloadCount.Value;

	public AttachmentFilterPanelModel FilterModel { get; }

	private readonly StatisticsRow statisticsEnqueued = new ("Enqueued");
	private readonly StatisticsRow statisticsDownloaded = new ("Downloaded");
	private readonly StatisticsRow statisticsFailed = new ("Failed");
	private readonly StatisticsRow statisticsSkipped = new ("Skipped");

	public ObservableCollection<StatisticsRow> StatisticsRows { get; }

	public bool IsDownloading => state.Downloader.IsDownloading;

	private readonly Window window;
	private readonly State state;
	private readonly ThrottledTask<int> enqueueDownloadItemsTask;
	private readonly ThrottledTask<DownloadStatusStatistics> downloadStatisticsTask;

	private readonly IDisposable attachmentCountSubscription;
	private readonly IDisposable downloadCountSubscription;

	private IDisposable? finishedItemsSubscription;
	private int doneItemsCount;
	private int totalEnqueuedItemCount;
	private int? totalItemsToDownloadCount;

	public AttachmentsPageModel() : this(null!, State.Dummy) {}

	public AttachmentsPageModel(Window window, State state) {
		this.window = window;
		this.state = state;

		FilterModel = new AttachmentFilterPanelModel(state);
		
		StatisticsRows = [
			statisticsEnqueued,
			statisticsDownloaded,
			statisticsFailed,
			statisticsSkipped
		];

		enqueueDownloadItemsTask = new ThrottledTask<int>(OnItemsEnqueued, TaskScheduler.FromCurrentSynchronizationContext());
		downloadStatisticsTask = new ThrottledTask<DownloadStatusStatistics>(UpdateStatistics, TaskScheduler.FromCurrentSynchronizationContext());

		attachmentCountSubscription = state.Db.Attachments.TotalCount.ObserveOn(AvaloniaScheduler.Instance).Subscribe(OnAttachmentCountChanged);
		downloadCountSubscription = state.Db.Downloads.TotalCount.ObserveOn(AvaloniaScheduler.Instance).Subscribe(OnDownloadCountChanged);

		RecomputeDownloadStatistics();
	}

	public void Dispose() {
		attachmentCountSubscription.Dispose();
		downloadCountSubscription.Dispose();
		finishedItemsSubscription?.Dispose();

		enqueueDownloadItemsTask.Dispose();
		downloadStatisticsTask.Dispose();

		FilterModel.Dispose();
	}

	private void OnAttachmentCountChanged(long newAttachmentCount) {
		if (IsDownloading) {
			EnqueueDownloadItemsLater();
		}
		else {
			RecomputeDownloadStatistics();
		}
	}

	private void OnDownloadCountChanged(long newDownloadCount) {
		RecomputeDownloadStatistics();
	}

	private async Task EnqueueDownloadItems() {
		try {
			OnItemsEnqueued(await state.Db.Downloads.EnqueueDownloadItems(CreateAttachmentFilter()));
		} catch (Exception e) {
			Log.Error(e);
			await Dialog.ShowOk(window, "Download Error", "Failed to enqueue items for download.");
		}
	}

	private void EnqueueDownloadItemsLater() {
		var filter = CreateAttachmentFilter();
		enqueueDownloadItemsTask.Post(cancellationToken => state.Db.Downloads.EnqueueDownloadItems(filter, cancellationToken));
	}

	private void OnItemsEnqueued(int itemCount) {
		totalEnqueuedItemCount += itemCount;
		totalItemsToDownloadCount = totalEnqueuedItemCount;
		UpdateDownloadMessage();
		RecomputeDownloadStatistics();
	}

	private AttachmentFilter CreateAttachmentFilter() {
		var filter = FilterModel.CreateFilter();
		filter.DownloadItemRule = AttachmentFilter.DownloadItemRules.OnlyNotPresent;
		return filter;
	}

	public async Task OnClickToggleDownload() {
		IsToggleDownloadButtonEnabled = false;

		if (IsDownloading) {
			await state.Downloader.Stop();

			finishedItemsSubscription?.Dispose();
			finishedItemsSubscription = null;

			RecomputeDownloadStatistics();

			await state.Db.Downloads.RemoveDownloadItems(EnqueuedItemFilter, FilterRemovalMode.RemoveMatching);

			doneItemsCount = 0;
			totalEnqueuedItemCount = 0;
			totalItemsToDownloadCount = null;
			UpdateDownloadMessage();
		}
		else {
			var finishedItems = await state.Downloader.Start();

			finishedItemsSubscription = finishedItems.Select(static _ => true)
			                                         .Buffer(TimeSpan.FromMilliseconds(100))
			                                         .Select(static items => items.Count)
			                                         .Where(static items => items > 0)
			                                         .ObserveOn(AvaloniaScheduler.Instance)
			                                         .Subscribe(OnItemsFinished);

			await EnqueueDownloadItems();
		}

		OnPropertyChanged(nameof(ToggleDownloadButtonText));
		OnPropertyChanged(nameof(IsDownloading));
		IsToggleDownloadButtonEnabled = true;
	}

	private void OnItemsFinished(int finishedItemCount) {
		doneItemsCount += finishedItemCount;
		UpdateDownloadMessage();
	}

	public async Task OnClickRetryFailedDownloads() {
		IsRetryingFailedDownloads = true;

		try {
			var allExceptFailedFilter = new DownloadItemFilter {
				IncludeStatuses = new HashSet<DownloadStatus> {
					DownloadStatus.Enqueued,
					DownloadStatus.Downloading,
					DownloadStatus.Success
				}
			};

			await state.Db.Downloads.RemoveDownloadItems(allExceptFailedFilter, FilterRemovalMode.KeepMatching);

			if (IsDownloading) {
				await EnqueueDownloadItems();
			}
		} catch (Exception e) {
			Log.Error(e);
		} finally {
			IsRetryingFailedDownloads = false;
		}
	}

	private void RecomputeDownloadStatistics() {
		downloadStatisticsTask.Post(state.Db.Downloads.GetStatistics);
	}

	private void UpdateStatistics(DownloadStatusStatistics statusStatistics) {
		statisticsEnqueued.Items = statusStatistics.EnqueuedCount;
		statisticsEnqueued.Size = statusStatistics.EnqueuedSize;

		statisticsDownloaded.Items = statusStatistics.SuccessfulCount;
		statisticsDownloaded.Size = statusStatistics.SuccessfulSize;

		statisticsFailed.Items = statusStatistics.FailedCount;
		statisticsFailed.Size = statusStatistics.FailedSize;

		statisticsSkipped.Items = statusStatistics.SkippedCount;
		statisticsSkipped.Size = statusStatistics.SkippedSize;

		HasFailedDownloads = statusStatistics.FailedCount > 0;

		UpdateDownloadMessage();
	}

	private void UpdateDownloadMessage() {
		DownloadMessage = IsDownloading ? doneItemsCount.Format() + " / " + (totalItemsToDownloadCount?.Format() ?? "?") : "";

		OnPropertyChanged(nameof(DownloadProgress));
	}

	[ObservableObject]
	public sealed partial class StatisticsRow(string state) {
		public string State { get; } = state;

		[ObservableProperty]
		private int items;

		[ObservableProperty]
		private ulong? size;
	}
}
