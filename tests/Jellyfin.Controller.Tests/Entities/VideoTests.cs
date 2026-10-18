using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

namespace Jellyfin.Controller.Tests.Entities;

[Collection("LibraryManagerTests")]
public class VideoTests
{
    private const string PartPath = "/movies/Movie (2004)/CD2/movie-b.strm";

    [Theory]
    [InlineData(false)] // the part resolves as a Movie and is rebuilt as a plain Video
    [InlineData(true)] // the part was saved without the flag by an earlier version
    public async Task RefreshedOwnedItems_StrmPart_KeepsShortcutFlag(bool savedBefore)
    {
        var previousLibrary = BaseItem.LibraryManager;
        var previousFileSystem = BaseItem.FileSystem;
        var previousProviders = BaseItem.ProviderManager;
        var previousLogger = BaseItem.Logger;
        var previousMediaSources = BaseItem.MediaSourceManager;
        var mediaSources = new Mock<IMediaSourceManager>();
        mediaSources.Setup(m => m.GetPathProtocol(It.IsAny<string>())).Returns(MediaProtocol.File);
        var partId = Guid.NewGuid();
        var library = new Mock<ILibraryManager>();
        library.Setup(l => l.GetNewItemId(PartPath, typeof(Video))).Returns(partId);
        library.Setup(l => l.GetContentType(It.IsAny<BaseItem>())).Returns(CollectionType.movies);
        if (savedBefore)
        {
            library.Setup(l => l.GetItemById(partId)).Returns(new Video { Id = partId, Path = PartPath });
        }
        else
        {
            library.Setup(l => l.ResolvePath(It.IsAny<FileSystemMetadata>(), It.IsAny<Folder>(), It.IsAny<IDirectoryService>(), It.IsAny<CollectionType?>()))
                .Returns(new Movie { Path = PartPath, IsShortcut = true });
        }

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.FileExists(PartPath)).Returns(true);
        fileSystem.Setup(f => f.GetFileSystemInfo(PartPath)).Returns(new FileSystemMetadata { FullName = PartPath, Exists = true });
        BaseItem? refreshedPart = null;
        var providers = new Mock<IProviderManager>();
        providers.Setup(p => p.RefreshSingleItem(It.IsAny<BaseItem>(), It.IsAny<MetadataRefreshOptions>(), It.IsAny<CancellationToken>()))
            .Callback<BaseItem, MetadataRefreshOptions, CancellationToken>((item, _, _) => refreshedPart ??= item)
            .ReturnsAsync(ItemUpdateType.None);

        try
        {
            BaseItem.LibraryManager = library.Object;
            BaseItem.FileSystem = fileSystem.Object;
            BaseItem.ProviderManager = providers.Object;
            BaseItem.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BaseItem>.Instance;
            BaseItem.MediaSourceManager = mediaSources.Object;
            var movie = new StackedMovie
            {
                Id = Guid.NewGuid(),
                Path = "/movies/Movie (2004)/CD1/movie-a.strm",
                IsShortcut = true,
                AdditionalParts = [PartPath]
            };

            await movie.RefreshOwnedItems(new MetadataRefreshOptions(Mock.Of<IDirectoryService>()), TestContext.Current.CancellationToken);

            var part = Assert.IsType<Video>(refreshedPart);
            Assert.True(part.IsShortcut);
        }
        finally
        {
            BaseItem.LibraryManager = previousLibrary;
            BaseItem.FileSystem = previousFileSystem;
            BaseItem.ProviderManager = previousProviders;
            BaseItem.Logger = previousLogger;
            BaseItem.MediaSourceManager = previousMediaSources;
        }
    }

    private sealed class StackedMovie : Movie
    {
        public Task<bool> RefreshOwnedItems(MetadataRefreshOptions options, CancellationToken cancellationToken)
            => RefreshedOwnedItems(options, new List<FileSystemMetadata>(), cancellationToken);
    }
}
