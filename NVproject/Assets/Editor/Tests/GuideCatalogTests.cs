using System.Collections.Generic;
using NUnit.Framework;
using NV.Game;
using NV.Game.UI;

namespace NV.Game.Tests
{
    /// <summary>
    /// 안내서 데이터의 무결성. 내용이 늘어나는 것은 자유지만, 빈 탭·빈 섹션·겹치는 id 는
    /// 그리는 쪽에서 빈 화면이나 죽은 탭으로만 드러나므로 여기서 먼저 잡는다.
    /// </summary>
    public sealed class GuideCatalogTests
    {
        [Test]
        public void 주제가_비어_있지_않다()
        {
            Assert.That(GuideCatalog.Topics.Count, Is.GreaterThan(0));
        }

        [Test]
        public void 주제_id_는_겹치지_않는다()
        {
            var seen = new HashSet<string>();

            foreach (var topic in GuideCatalog.Topics)
            {
                Assert.That(string.IsNullOrWhiteSpace(topic.Id), Is.False);
                Assert.That(seen.Add(topic.Id), Is.True, $"중복 id: {topic.Id}");
            }
        }

        [Test]
        public void 모든_주제는_제목과_내용을_가진다()
        {
            foreach (var topic in GuideCatalog.Topics)
            {
                Assert.That(string.IsNullOrWhiteSpace(topic.Title), Is.False, topic.Id);
                Assert.That(topic.Sections.Length, Is.GreaterThan(0), topic.Id);

                foreach (var section in topic.Sections)
                {
                    Assert.That(string.IsNullOrWhiteSpace(section.Heading), Is.False, topic.Id);
                    Assert.That(section.Lines.Length, Is.GreaterThan(0),
                        $"{topic.Id} / {section.Heading}");

                    foreach (var line in section.Lines)
                        Assert.That(string.IsNullOrWhiteSpace(line), Is.False,
                            $"{topic.Id} / {section.Heading}");
                }
            }
        }

        /// <summary>역할별 첫 탭은 실제로 존재하는 탭이어야 한다 — 없는 id 는 빈 내용으로 열린다.</summary>
        [Test]
        public void 역할별_첫_탭은_존재하는_주제다()
        {
            foreach (var role in new[] { Role.Unassigned, Role.Runner, Role.Seeker })
            {
                var id = GuideCatalog.TopicFor(role);
                var found = false;

                foreach (var topic in GuideCatalog.Topics)
                    if (topic.Id == id) found = true;

                Assert.That(found, Is.True, $"{role} → {id}");
            }
        }
    }
}
