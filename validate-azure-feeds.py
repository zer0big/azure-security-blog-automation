#!/usr/bin/env python3
"""Azure 피드 URL 검증"""
import requests
import sys

FEEDS_AZURE = [
    ("Azure Updates", "https://azure.microsoft.com/en-us/updates/feed/"),
    ("Azure DevOps Blog", "https://devblogs.microsoft.com/devops/feed/"),
    ("Azure Architecture Blog", "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureArchitectureBlog"),
    ("Azure Infrastructure Blog", "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureInfrastructureBlog"),
    ("Azure Governance Blog", "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureGovernanceandManagementBlog"),
    ("Azure DevOps Community", "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureDevOpsCommunity"),
    ("Azure Integration Blog", "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=IntegrationsonAzureBlog")
]

print("=" * 70)
print("Azure RSS 피드 검증")
print("=" * 70 + "\n")

valid_feeds = []
invalid_feeds = []

for name, url in FEEDS_AZURE:
    try:
        print(f"📡 {name}...", end=" ")
        resp = requests.get(url, timeout=10)
        
        # XML 확인
        content_lower = resp.text.lower().strip()
        is_xml = content_lower.startswith('<?xml') or content_lower.startswith('<rss') or '<rss' in content_lower[:200]
        is_html = content_lower.startswith('<!doctype html') or content_lower.startswith('<html')
        
        if is_html:
            print(f"❌ HTML 반환 (XML 아님)")
            invalid_feeds.append((name, url, "HTML instead of XML"))
        elif not is_xml:
            print(f"❌ XML 형식 아님")
            invalid_feeds.append((name, url, "Not XML format"))
        else:
            # 엔트리 개수 확인
            entry_count = resp.text.count('<item>') + resp.text.count('<entry>')
            print(f"✅ OK ({entry_count} entries)")
            valid_feeds.append((name, url))
            
    except Exception as e:
        print(f"❌ 오류: {str(e)[:50]}")
        invalid_feeds.append((name, url, str(e)))

print("\n" + "=" * 70)
print(f"✅ 정상: {len(valid_feeds)}개")
print(f"❌ 실패: {len(invalid_feeds)}개")

if invalid_feeds:
    print("\n🚨 문제 피드:")
    for name, url, reason in invalid_feeds:
        print(f"  - {name}: {reason}")
        print(f"    URL: {url}")

print("\n✅ 사용 가능한 피드:")
for name, url in valid_feeds:
    print(f'  {{"sourceName": "{name}", "emoji": "...", "url": "{url}"}},')

sys.exit(0 if len(invalid_feeds) == 0 else 1)
