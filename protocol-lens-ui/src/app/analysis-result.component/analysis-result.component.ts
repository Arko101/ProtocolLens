import { CommonModule } from '@angular/common';
import { Component, Input, signal } from '@angular/core';
import { AnalysisResponse, ConversationDto, FindingDto } from '../analysis-response.model';

@Component({
  selector: 'app-analysis-result',
  imports: [CommonModule],
  templateUrl: './analysis-result.component.html',
  styleUrls: ['./analysis-result.component.css'],
})
export class AnalysisResultComponent {
  private _result: AnalysisResponse | null = null;

  @Input()
  set result(value: AnalysisResponse | null) {
    this._result = value;
    this.selectedConversationId.set(null);
    this.filterText.set('');
  }
  get result(): AnalysisResponse | null {
    return this._result;
  }

  selectedConversationId = signal<string | null>(null);
  filterText = signal('');

  // Bounded rendering: only show the first N conversations in the list to keep the DOM small
  // for larger results; the full export still contains everything.
  readonly maxRenderedConversations = 200;

  get filteredConversations(): ConversationDto[] {
    if (!this.result) {
      return [];
    }
    const filter = this.filterText().trim().toLowerCase();
    const all = this.result.conversations;
    const filtered = filter
      ? all.filter(
          (c) =>
            c.transactionId.toLowerCase().includes(filter) ||
            c.flow.toLowerCase().includes(filter) ||
            (c.identityEvidence ?? '').toLowerCase().includes(filter)
        )
      : all;
    return filtered.slice(0, this.maxRenderedConversations);
  }

  get selectedConversation(): ConversationDto | undefined {
    const id = this.selectedConversationId();
    return this.result?.conversations.find((c) => c.conversationId === id);
  }

  findingsFor(conversationId: string): FindingDto[] {
    return this.result?.findings.filter((f) => f.conversationId === conversationId) ?? [];
  }

  selectConversation(id: string): void {
    this.selectedConversationId.set(id);
  }

  onFilterInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.filterText.set(input.value);
  }

  /** Relative time in seconds from the conversation's first observation, for timeline display. */
  relativeSeconds(conversation: ConversationDto, timestamp: string): number {
    const first = conversation.observations[0]?.timestamp;
    if (!first) {
      return 0;
    }
    return (new Date(timestamp).getTime() - new Date(first).getTime()) / 1000;
  }

  exportJson(): void {
    if (!this.result) {
      return;
    }
    const blob = new Blob([JSON.stringify(this.result, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.result.fileName || 'capture'}-analysis.json`;
    a.click();
    URL.revokeObjectURL(url);
  }
}
