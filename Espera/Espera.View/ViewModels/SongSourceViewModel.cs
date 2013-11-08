﻿using Espera.Core.Management;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Espera.View.ViewModels
{
    public abstract class SongSourceViewModel<T> : ReactiveObject, ISongSourceViewModel
        where T : SongViewModelBase
    {
        private readonly ObservableAsPropertyHelper<bool> isAdmin;
        private readonly Library library;
        private readonly Subject<Unit> timeoutWarning;
        private string searchText;
        private IEnumerable<T> selectableSongs;
        private IEnumerable<SongViewModelBase> selectedSongs;

        protected SongSourceViewModel(Library library)
        {
            this.library = library;

            this.searchText = String.Empty;
            this.selectableSongs = Enumerable.Empty<T>();
            this.timeoutWarning = new Subject<Unit>();

            IObservable<bool> canAddToPlaylist = this.WhenAnyValue(x => x.SelectedSongs, x => x != null && x.Any());
            this.AddToPlaylistCommand = new ReactiveCommand(canAddToPlaylist);
            this.AddToPlaylistCommand.Subscribe(p =>
            {
                if (!this.Library.CanAddSongToPlaylist)
                {
                    // Trigger the animation
                    this.timeoutWarning.OnNext(Unit.Default);

                    return;
                }

                if (this.IsAdmin)
                {
                    this.library.AddSongsToPlaylist(this.SelectedSongs.Select(song => song.Model));
                }

                else
                {
                    this.library.AddSongToPlaylist(this.SelectedSongs.Select(song => song.Model).Single());
                }
            });

            this.SelectionChangedCommand = new ReactiveCommand();
            this.SelectionChangedCommand.Where(x => x != null)
                .Select(x => ((IEnumerable)x).Cast<T>())
                .Subscribe(x => this.SelectedSongs = x);

            this.isAdmin = this.Library.AccessMode
                .Select(x => x == AccessMode.Administrator)
                .ToProperty(this, x => x.IsAdmin);
        }

        public IReactiveCommand AddToPlaylistCommand { get; private set; }

        public bool IsAdmin
        {
            get { return this.isAdmin.Value; }
        }

        public abstract IReactiveCommand PlayNowCommand { get; }

        public string SearchText
        {
            get { return this.searchText; }
            set { this.RaiseAndSetIfChanged(ref this.searchText, value); }
        }

        public IEnumerable<T> SelectableSongs
        {
            get { return this.selectableSongs; }
            protected set { this.RaiseAndSetIfChanged(ref this.selectableSongs, value); }
        }

        public IEnumerable<SongViewModelBase> SelectedSongs
        {
            get { return this.selectedSongs; }
            set { this.RaiseAndSetIfChanged(ref this.selectedSongs, value); }
        }

        public ReactiveCommand SelectionChangedCommand { get; set; }

        public IObservable<Unit> TimeoutWarning
        {
            get { return this.timeoutWarning.AsObservable(); }
        }

        protected Library Library
        {
            get { return this.library; }
        }

        protected Func<IEnumerable<T>, IOrderedEnumerable<T>> SongOrderFunc { get; private set; }

        protected void ApplyOrder(Func<SortOrder, Func<IEnumerable<T>, IOrderedEnumerable<T>>> orderFunc, ref SortOrder sortOrder)
        {
            this.SongOrderFunc = orderFunc(sortOrder);
            SortHelpers.InverseOrder(ref sortOrder);

            this.SelectableSongs = this.SongOrderFunc(this.SelectableSongs);
        }
    }
}