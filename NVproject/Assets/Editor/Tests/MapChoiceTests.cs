using System.Collections.Generic;
using NUnit.Framework;
using NV.Client.Lobby.Models;
using NV.Client.Map;
using NV.Client.Net.Session;
using UnityEngine;

namespace NV.Client.EditorTools.Tests
{
    /// <summary>
    /// The four states a map row can be in, and the rule that produces each.
    ///
    /// **This is where the create-room screen's honesty is enforced.** The server's list and this
    /// build's catalog are different answers to different questions — what maps exist, and what
    /// maps this player can draw — and merging them wrongly fails in ways nobody sees until after
    /// connecting: a row that cannot render, or one whose terrain differs from the server's.
    /// </summary>
    public sealed class MapChoiceTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = 0; index < _spawned.Count; index++)
            {
                if (_spawned[index] != null) Object.DestroyImmediate(_spawned[index]);
            }

            _spawned.Clear();
        }

        [Test]
        public void 양쪽에_있고_해시가_같으면_만들_수_있다()
        {
            var choices = MapChoices.Merge(
                new[] { Server("alpha", 7u) }, true, Catalog(Entry("alpha", 7u)));

            Assert.AreEqual(1, choices.Count);
            Assert.AreEqual(MapChoiceStatus.Ready, choices[0].Status);
            Assert.IsTrue(choices[0].CanCreate);
            Assert.AreEqual(string.Empty, choices[0].Reason);
        }

        /// WebGL 빌드는 에셋을 구워서 나가므로 **서버에 맵을 추가하는 것만으로는 이미 배포된
        /// 클라이언트가 그것을 그릴 수 없다.** 목록에서 빼지 않고 이유를 붙여 남긴다.
        [Test]
        public void 서버에만_있으면_이_빌드에_없다고_말한다()
        {
            // 카탈로그가 **있고** 그 안에 이 맵이 없어야 그렇게 말할 수 있다. 빈 카탈로그는
            // "이 맵이 없다" 가 아니라 "아는 것이 없다" 다.
            var choices = MapChoices.Merge(
                new[] { Server("alpha", 7u) }, true, Catalog(Entry("other", 1u)));

            var alpha = choices.Find(choice => choice.MapId == "alpha");

            Assert.AreEqual(MapChoiceStatus.MissingLocally, alpha.Status);
            Assert.IsFalse(alpha.CanCreate);
            Assert.IsNotEmpty(alpha.Reason);
        }

        [Test]
        public void 카탈로그에만_있으면_서버에_없다고_말한다()
        {
            var choices = MapChoices.Merge(
                new ServerMapInfo[0], true, Catalog(Entry("alpha", 7u)));

            Assert.AreEqual(1, choices.Count);
            Assert.AreEqual(MapChoiceStatus.MissingOnServer, choices[0].Status);
            Assert.IsFalse(choices[0].CanCreate);
        }

        /// **이 검사가 이 작업이 덤으로 가져오는 것이다.** 지금 이 상황은 접속한 뒤 맵 해시
        /// 불일치 경고 한 줄로만 드러나고, 그때 사람은 이미 방을 만들었다.
        [Test]
        public void 해시가_다르면_지형이_다르다고_말한다()
        {
            var choices = MapChoices.Merge(
                new[] { Server("alpha", 7u) }, true, Catalog(Entry("alpha", 9u)));

            Assert.AreEqual(MapChoiceStatus.HashMismatch, choices[0].Status);
            Assert.IsFalse(choices[0].CanCreate);
        }

        /// 에셋은 있는데 그릴 것이 없는 줄(프리팹도 씬 재정의도 없다)은 그릴 수 없는 것과 같다.
        [Test]
        public void 그릴_수_없는_줄은_이_빌드에_없는_것이다()
        {
            var entry = Entry("alpha", 7u);
            entry.prefab = null;
            entry.sceneOverride = string.Empty;

            var choices = MapChoices.Merge(new[] { Server("alpha", 7u) }, true, Catalog(entry));

            Assert.AreEqual(MapChoiceStatus.MissingLocally, choices[0].Status);
        }

        /// **목록을 못 받은 것과 "서버에 맵이 없다" 는 다르다.** 구분하지 않으면 서버가 꺼져
        /// 있을 때 이 빌드의 모든 맵이 "서버에 없다" 로 뜨고, 방을 아예 만들 수 없게 된다.
        [Test]
        public void 서버가_답하지_않았으면_카탈로그만으로_만들_수_있다()
        {
            var choices = MapChoices.Merge(
                new ServerMapInfo[0], false, Catalog(Entry("alpha", 7u)));

            Assert.AreEqual(1, choices.Count);
            Assert.AreEqual(MapChoiceStatus.Ready, choices[0].Status);
            Assert.IsTrue(choices[0].CanCreate);
        }

        /// **카탈로그가 없는 빌드를 막지 않는다.** 없다는 것은 지역 지식이 없다는 뜻이고,
        /// 서버가 답하지 않은 경우의 반대편이다. 막으면 카탈로그를 굽기 전의 빌드가 방을
        /// 아예 만들 수 없게 되는데, 씬 표만으로 열리는 맵이 지금도 있다.
        [Test]
        public void 카탈로그가_없으면_서버의_맵을_그대로_쓴다()
        {
            var empty = MapChoices.Merge(new[] { Server("alpha", 7u) }, true, Catalog());
            var missing = MapChoices.Merge(new[] { Server("alpha", 7u) }, true, null);

            Assert.AreEqual(MapChoiceStatus.Ready, empty[0].Status);
            Assert.AreEqual(MapChoiceStatus.Ready, missing[0].Status);
        }

        [Test]
        public void 만들_수_있는_것과_기본_맵이_앞에_온다()
        {
            var choices = MapChoices.Merge(
                new[]
                {
                    Server("zulu", 1u, isDefault: false),
                    Server("alpha", 2u, isDefault: true),
                    Server("ghost", 3u),
                },
                true,
                Catalog(Entry("alpha", 2u), Entry("zulu", 1u)));

            Assert.AreEqual("alpha", choices[0].MapId, "기본 맵이 먼저다");
            Assert.AreEqual("zulu", choices[1].MapId);
            Assert.AreEqual("ghost", choices[2].MapId, "만들 수 없는 것이 뒤로 간다");
        }

        /// 표시용 이름의 출처는 맵 파일이고 서버가 그것을 읽는다. 카탈로그의 사본은
        /// 마지막으로 구웠을 때의 것이므로 서버 것을 먼저 쓴다.
        [Test]
        public void 표시용_이름은_서버_것을_먼저_쓴다()
        {
            var entry = Entry("alpha", 7u);
            entry.displayName = "옛 이름";

            var choices = MapChoices.Merge(
                new[] { Server("alpha", 7u, displayName: "새 이름") }, true, Catalog(entry));

            Assert.AreEqual("새 이름", choices[0].DisplayName);
        }

        /// 격자가 없는 맵은 만들 수는 있으나 매치가 성립하지 않는다 — 열쇠도 문도 생기지
        /// 않는다. 그것을 막지 않고 말한다: 개발용 맵으로 방을 여는 것은 정상 행위다.
        [Test]
        public void 격자가_없는_맵은_만들_수_있고_경고만_한다()
        {
            var choices = MapChoices.Merge(
                new[] { Server("alpha", 7u, supportsMatch: false) }, true, Catalog(Entry("alpha", 7u)));

            Assert.IsTrue(choices[0].CanCreate);
            Assert.IsNotEmpty(choices[0].Reason);
        }

        [Test]
        public void 이름_없는_줄은_id_로_보인다()
        {
            var entry = Entry("alpha", 7u);
            entry.displayName = string.Empty;

            var choices = MapChoices.Merge(
                new[] { Server("alpha", 7u, displayName: string.Empty) }, true, Catalog(entry));

            Assert.AreEqual("alpha", choices[0].DisplayName);
        }

        // ==================================================== 배포된 카탈로그

        /// <summary>
        /// The catalog in this project has to describe every baked map, or the lobby lists a map it
        /// cannot draw. It is written by the bake pipeline, so a missing row means somebody baked
        /// before that wiring existed — or edited the asset by hand.
        /// </summary>
        [Test]
        public void 구운_맵마다_카탈로그에_줄이_있다()
        {
            var catalog = MapCatalog.Load();

            if (catalog == null)
            {
                Assert.Ignore("이 프로젝트에 아직 MapCatalog 가 없다. 맵을 한 번 구우면 생긴다.");
                return;
            }

            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:MapBakedAsset"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MapBakedAsset>(path);

                if (asset == null) continue;

                var entry = catalog.Find(asset.MapName);

                Assert.IsNotNull(entry, $"구운 맵 '{asset.MapName}' 이 카탈로그에 없다 ({path}).");
                Assert.AreEqual(asset, entry.asset, $"'{asset.MapName}' 의 카탈로그 줄이 다른 에셋을 가리킨다.");
            }
        }

        /// <summary>Every row has to be drawable, or the lobby offers a map that cannot open.</summary>
        [Test]
        public void 카탈로그의_모든_줄이_그릴_수_있다()
        {
            var catalog = MapCatalog.Load();

            if (catalog == null)
            {
                Assert.Ignore("이 프로젝트에 아직 MapCatalog 가 없다.");
                return;
            }

            foreach (var entry in catalog.Entries)
            {
                Assert.IsNotEmpty(entry.mapId, "맵 id 가 빈 줄이 있다.");
                Assert.IsTrue(
                    entry.IsPlayable,
                    $"'{entry.mapId}' 은 에셋과 프리팹(또는 씬 재정의) 중 하나가 없어 그릴 수 없다.");
            }
        }

        // ==================================================== 도구

        private static ServerMapInfo Server(
            string id,
            uint hash,
            bool isDefault = false,
            bool supportsMatch = true,
            string displayName = null)
        {
            return new ServerMapInfo(
                id,
                displayName ?? id,
                string.Empty,
                hash,
                isDefault,
                supportsMatch,
                2,
                35,
                35,
                2,
                8);
        }

        private MapCatalogEntry Entry(string id, uint hash)
        {
            var asset = ScriptableObject.CreateInstance<MapBakedAsset>();
            _spawned.Add(asset);

            var prefab = new GameObject(id + "-prefab");
            _spawned.Add(prefab);

            return new MapCatalogEntry
            {
                mapId = id,
                asset = asset,
                prefab = prefab,
                displayName = id,
                bakedHash = unchecked((int)hash),
            };
        }

        private MapCatalog Catalog(params MapCatalogEntry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<MapCatalog>();
            _spawned.Add(catalog);

            catalog.Replace(entries);

            return catalog;
        }
    }
}
