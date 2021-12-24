using Cysharp.Threading.Tasks;
using Elselam.UnityRouter.Domain;
using Elselam.UnityRouter.History;
using Elselam.UnityRouter.SceneLoad;
using Elselam.UnityRouter.Transitions;
using Elselam.UnityRouter.Url;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

namespace Elselam.UnityRouter.Installers
{
    public class NavigationManager : INavigation
    {
        private readonly IHistory history;
        private readonly IUrlManager urlManager;
        private readonly ICurrentScreen currentScreen;
        private readonly ITransition defaultTransition;
        private readonly ISceneLoader sceneLoader;
        private readonly IScreenResolver screenResolver;
        private readonly List<IScreenRegistry> screenRegistries;
        private readonly IScreenFactory screenFactory;
        private bool loading;
        private readonly Dictionary<string, IScreenModel> screenModels;

        [Inject]
        public NavigationManager(List<IScreenRegistry> screenRegistries,
            IScreenFactory screenFactory,
            IScreenResolver screenResolver,
            ISceneLoader sceneLoader,
            IUrlManager urlManager,
            IHistory history,
            ICurrentScreen currentScreen)
        {
            this.history = history;
            this.screenFactory = screenFactory;
            this.screenResolver = screenResolver;
            this.screenRegistries = screenRegistries;
            this.sceneLoader = sceneLoader;
            this.urlManager = urlManager;
            this.currentScreen = currentScreen;

            defaultTransition = new DefaultTransition();

            screenResolver.Initialize();
            screenModels = new Dictionary<string, IScreenModel>();
        }

        public void Initialize()
        {
            foreach (var screenRegistry in screenRegistries)
            {
                screenModels[screenRegistry.ScreenId] = screenFactory.Create(screenRegistry);
            }
            NavigateTo(screenResolver.ResolveScheme(), null, false);
        }

        public void NavigateTo<TScreen>(ITransition transition = null, IDictionary<string, string> parameters = null) where TScreen : IScreenInteractor
            => NavigateTo(typeof(TScreen), transition, parameters);

        public void NavigateTo(Type screenType, ITransition transition = null, IDictionary<string, string> parameters = null)
        {
            var screenName = GetScreenName(screenType);
            if (string.IsNullOrEmpty(screenName))
            {
                throw new NavigationException($"Screen Registry of type: {screenType} needs to be registered");
            }
            NavigateTo(urlManager.BuildToScheme(screenName, parameters), transition);
        }

        private bool TryNavigateToScene(ScreenScheme scheme)
        {
            if (Application.CanStreamedLevelBeLoaded(scheme.ScreenId))
            {
                NavigateTo(scheme.ScreenId,
                    container => container.Bind<ScreenScheme>().FromInstance(scheme));
                return true;
            }
            return false;
        }

        public async void NavigateTo(string sceneName, Action<DiContainer> extraBindings = null)
        {
            sceneLoader.LoadLoadingScene();
            await sceneLoader.LoadScene(sceneName, extraBindings);
            sceneLoader.UnloadLoadingScene();

            UnloadScreenToScene(sceneName, false);
        }

        public async void BackToMainScene()
        {
            sceneLoader.LoadLoadingScene();
            await sceneLoader.LoadMainScene();
            sceneLoader.UnloadLoadingScene();

            BackToLastScreen();
        }

        public void BackToLastScreen(ITransition transition = null)
        {
            var scheme = history.Back();
            if (scheme == null)
                throw new NavigationException($"{nameof(scheme)} cannot be null");

            if (scheme is SceneScheme sceneScheme)
            {
                UnloadScreenToScene(sceneScheme.ScreenId, true);
            }
            else
            {
                NavigateTo(scheme, transition, true);
            }
        }

        public void NavigateTo(ScreenScheme enterScheme, ITransition transition = null)
        {
            NavigateTo(enterScheme, transition, false);
        }

        private void NavigateTo(ScreenScheme enterScheme, ITransition transition = null, bool back = false)
        {
            if (loading) //safe guard to avoid concurrent loadings
                return;
            loading = true;

            var enterScreenModel = GetScreenInstances(enterScheme.ScreenId);
            if (enterScreenModel == null)
            {
                if (TryNavigateToScene(enterScheme))
                    return;

                throw new NavigationException($"No screen or scene with name: {enterScheme.ScreenId} found");
            }

            var exitScreenModel = GetScreenInstances(currentScreen.Scheme?.ScreenId);
            if (exitScreenModel != null)
            {
                UnloadExitScreen(exitScreenModel.Interactor, back);
                Transition(transition, enterScreenModel.Presenter, exitScreenModel?.Presenter ?? null);
            }
            else
            {
                Transition(transition, enterScreenModel.Presenter, null);
            }

            LoadEnterScreen(enterScreenModel.Interactor, enterScheme);

            loading = false;
        }

        private void UnloadScreenToScene(string sceneName, bool back)
        {
            if (currentScreen.Screen != null)
            {
                UnloadExitScreen(currentScreen.Screen, back);
                var exitScreen = GetScreenInstances(currentScreen.Scheme.ScreenId).Presenter;
                Transition(defaultTransition, null, exitScreen);
            }

            currentScreen.SetCurrentScreen(null, new SceneScheme(string.Empty, sceneName));
        }

        private async UniTask Transition(ITransition transition, IScreenPresenter enter, IScreenPresenter exit)
        {
            var transitionInstance = transition ?? defaultTransition;
            await transitionInstance.Transite(enter, exit);
        }

        private void UnloadExitScreen(IScreenInteractor exitInteractor, bool back)
        {
            var parameters = exitInteractor.OnExit();
            if (!back)
            {
                var name = GetScreenName(exitInteractor.GetType());
                var url = urlManager.BuildToString(name, parameters);
                var success = history.Add(urlManager.Deserialize(url));
                if (!success)
                {
                    throw new NavigationException($"Couldn't add {exitInteractor.GetType()} to history");
                }
            }
        }

        private void LoadEnterScreen(IScreenInteractor enterInteractor, ScreenScheme enterScheme)
        {
            currentScreen.SetCurrentScreen(enterInteractor, enterScheme);
            if (enterScheme.Parameters?.Count > 0)
            {
                enterInteractor.WithParameters(enterScheme.Parameters);
                enterInteractor.OnEnter();
            }
            else
            {
                enterInteractor.OnEnter();
            }
        }

        private string GetScreenName(Type controllerType)
        {
            return screenRegistries.FirstOrDefault(s => s.ScreenInteractor == controllerType)?.ScreenId;
        }

        private IScreenModel GetScreenInstances(string screenName)
        {
            if (screenName == null)
                return null;
            return screenModels.TryGetValue(screenName, out var value) ? value : null;
        }
    }
}